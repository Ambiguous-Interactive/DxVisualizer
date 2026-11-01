#!/usr/bin/env python3
"""Keep the repository's uv project environment usable (issues #509, #511).

``uv`` refuses to run a project command when the project environment exists
but is not a virtual environment. An interrupted first bootstrap (a closed
terminal, a killed ``uv run``, a cancelled IDE "create environment") leaves
the partially built directory behind, and on Windows that residue is a copied
``.venv/Scripts/python.exe`` with no ``pyvenv.cfg``. Every later ``uv run``
then dies before Python starts:

    failed to locate pyvenv.cfg: The system cannot find the file specified.

The message names a file the user never created, and it stops every npm
script at once, because the entire harness reaches uv through
``uv run scripts/...``. uv itself offers no repair: ``uv sync``, ``uv run
--no-sync``, and ``uv run --frozen`` all reject the directory, while ``uv venv
--clear`` refuses to clear a directory it does not recognize. The supported
recovery is to remove the gitignored directory and let uv rebuild it from the
committed ``uv.lock``, which is what this script automates.

This script therefore runs *without* the project environment
(``uv run --no-project scripts/uv_environment.py``), so it still works while
the environment is broken:

- ``--check`` exits 1 and names the missing markers plus the one-command fix.
- ``--repair`` removes an unusable environment, rebuilds it with ``uv sync
  --locked``, and fails loudly with uv's own output when the rebuild fails.
- ``--json`` prints the same verdict for tools.

A missing environment is healthy: uv creates it on the next run. Every npm
script that reaches uv runs ``--repair`` first, so a corrupted environment
costs one rebuild instead of a dead end.

``UV_PROJECT_ENVIRONMENT`` is honored, because it moves the environment the
same way it moves uv's. It can be relative (to the project) or absolute. That
variable is user input, so the repair only deletes or moves a directory inside
the project: an outside path is rebuilt in place and keeps its residue, and a
path equal to the project root or one of its parents is refused before any
file changes.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, List, Mapping, Optional, Sequence, Tuple

sys.dont_write_bytecode = True

REPO_ROOT = Path(__file__).resolve().parent.parent
OWNER = "scripts/uv_environment.py"
DEFAULT_ENVIRONMENT = ".venv"
REPAIR_COMMAND = "npm run env:repair"

STATE_MISSING = "missing"
STATE_VALID = "valid"
STATE_INVALID = "invalid"

Message = str
Runner = Callable[[Sequence[str], Path], Tuple[int, str]]


def display_path(path: Path) -> str:
    """Show a repo-relative path when inside the repository, else the absolute path."""
    try:
        relative = os.path.relpath(path, REPO_ROOT).replace(os.sep, "/")
    except ValueError:
        return str(path)
    return str(path) if relative.startswith("../") else relative


def environment_directory(root: Path, environment: Optional[Mapping[str, str]] = None) -> Path:
    """Resolve the project environment uv would use for this root."""
    values = os.environ if environment is None else environment
    configured = values.get("UV_PROJECT_ENVIRONMENT") or DEFAULT_ENVIRONMENT
    candidate = Path(configured).expanduser()
    return candidate if candidate.is_absolute() else root / candidate


@dataclass(frozen=True)
class Containment:
    """Where the project environment sits relative to the project root."""

    inside_root: bool
    fatal: bool
    reason: Message = ""


def resolve_existing(path: Path) -> Path:
    """Resolve the deepest existing part of a path, so a symlink loop always raises.

    ``Path.resolve(strict=False)`` can swallow a symlink-loop error on Python
    3.13 and later, which would classify an unverifiable path as safe to
    delete. Strict resolution keeps the error.

    A missing path resolves through its nearest existing parent, so a first run
    is not treated as a failure, and the reconstructed result is normalized so
    ``missing/..`` classifies as the parent it names instead of its own text.
    A dangling symlink resolves to its link target, so a link that points
    outside the project keeps the outside-path protections instead of being
    unlinked as if it were a local directory.
    """
    try:
        return path.resolve(strict=True)
    except FileNotFoundError:
        pass
    if path.is_symlink():
        target = Path(os.readlink(path))
        if not target.is_absolute():
            target = path.parent / target
        return resolve_existing(target)
    parent = path.parent
    if parent == path:
        return path
    return Path(os.path.normpath(str(resolve_existing(parent) / path.name)))


def containment(root: Path, environment: Path) -> Containment:
    """Classify the environment path so a repair never deletes the wrong tree.

    ``UV_PROJECT_ENVIRONMENT`` is user input. Pointed at the repository, a
    parent, or an unrelated directory, a "repair" would delete the wrong tree,
    so the classification decides what the repair may do: a path inside the
    project can be deleted or moved, an outside path is rebuilt in place, and
    the project root or one of its parents is refused outright.
    """
    try:
        resolved_root = resolve_existing(root)
        resolved_environment = resolve_existing(environment)
    except (OSError, RuntimeError) as error:
        # A symlink loop raises RuntimeError on some Python versions, and an
        # unreadable path raises OSError. Neither proves the path is safe, so
        # the repair must not delete or move anything it could not verify.
        return Containment(
            inside_root=False,
            fatal=True,
            reason=f"the project environment path '{display_path(environment)}' could not be resolved ({error})",
        )
    if resolved_environment == resolved_root:
        return Containment(
            inside_root=False,
            fatal=True,
            reason=f"the project environment resolves to the project root '{display_path(resolved_root)}'",
        )
    if resolved_environment in resolved_root.parents:
        return Containment(
            inside_root=False,
            fatal=True,
            reason=f"the project environment resolves to '{display_path(resolved_environment)}', a parent of the project",
        )
    if resolved_root in resolved_environment.parents:
        return Containment(inside_root=True, fatal=False)
    return Containment(
        inside_root=False,
        fatal=False,
        reason=f"the project environment resolves outside the project, to '{display_path(resolved_environment)}'",
    )


def interpreter_candidates(environment: Path) -> Tuple[Path, ...]:
    """List the interpreter names uv requires for this platform's venv layout."""
    if os.name == "nt":
        return (environment / "Scripts" / "python.exe", environment / "Scripts" / "python3.exe")
    binary_directory = environment / "bin"
    versioned = sorted(path for path in binary_directory.glob("python3.*") if path.is_file())
    return (binary_directory / "python", binary_directory / "python3", *versioned)


def layout_directory() -> Message:
    """Name the platform's venv binary directory, for failure messages."""
    return "Scripts" if os.name == "nt" else "bin"


@dataclass(frozen=True)
class EnvironmentReport:
    """What one inspection found, with the evidence behind the verdict."""

    root: Path
    path: Path
    state: Message
    problems: Tuple[Message, ...] = ()
    repairable: bool = True

    @property
    def needs_repair(self) -> bool:
        return self.state == STATE_INVALID

    def to_json(self) -> Dict[str, object]:
        return {
            "owner": OWNER,
            "state": self.state,
            "root": str(self.root),
            "path": str(self.path),
            "problems": list(self.problems),
        }


def foreign_platform_problem(environment: Path) -> Optional[Message]:
    """Name another operating system when it built this environment.

    The repository checkout is shared between the Windows host and the Linux
    container on developer machines, so `.venv` can flip owners: the last
    platform to run a command rewrites `pyvenv.cfg` for itself, and the other
    platform then reports a broken interpreter. Naming the other platform
    makes that rebuild explainable instead of looking like random corruption.
    """
    try:
        text = (environment / "pyvenv.cfg").read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None
    values: List[Message] = []
    for line in text.splitlines():
        key, _, value = line.partition("=")
        if key.strip() in ("home", "executable", "base-executable", "base-prefix", "base-home"):
            values.append(value.strip())
    styles: Dict[Message, bool] = {
        "windows": any(re.match(r"^[A-Za-z]:[\\/]", value) or value.startswith("\\\\") for value in values),
        "posix": any(value.startswith("/") for value in values),
    }
    # Any path from the other platform proves the file describes a foreign
    # interpreter, even when a second key names this platform.
    foreign_style = "posix" if os.name == "nt" else "windows"
    if not styles[foreign_style]:
        return None
    names: Dict[Message, Message] = {"windows": "Windows", "posix": "POSIX"}
    patterns: Dict[Message, Message] = {"windows": r"^[A-Za-z]:[\\/]", "posix": r"^/"}
    other = names[foreign_style]
    example = next((value for value in values if re.match(patterns[foreign_style], value) or value.startswith("\\\\")), "")
    return f"pyvenv.cfg describes a {other} interpreter ('{example}'), so this environment was built on another operating system"


def inspect_project_environment(root: Path, environment: Optional[Mapping[str, str]] = None) -> EnvironmentReport:
    """Report whether uv can use this root's project environment as it stands."""
    environment_path = environment_directory(root, environment)
    position = containment(root, environment_path)
    if position.fatal:
        return EnvironmentReport(
            root=root,
            path=environment_path,
            state=STATE_INVALID,
            problems=(position.reason + "; point UV_PROJECT_ENVIRONMENT at a directory inside the project, or unset it",),
            repairable=False,
        )
    if not environment_path.exists() and not environment_path.is_symlink():
        return EnvironmentReport(root=root, path=environment_path, state=STATE_MISSING)
    if not environment_path.is_dir():
        return EnvironmentReport(
            root=root,
            path=environment_path,
            state=STATE_INVALID,
            problems=("the project environment path exists but is not a directory",),
        )

    problems: List[Message] = []
    if not (environment_path / "pyvenv.cfg").is_file():
        problems.append(f"pyvenv.cfg is missing at '{display_path(environment_path / 'pyvenv.cfg')}'")
    else:
        foreign = foreign_platform_problem(environment_path)
        if foreign is not None:
            problems.append(foreign)
    if not any(candidate.is_file() for candidate in interpreter_candidates(environment_path)):
        problems.append(f"no Python interpreter is present under '{layout_directory()}' (uv reports this as a missing Python executable)")
    state = STATE_INVALID if problems else STATE_VALID
    return EnvironmentReport(root=root, path=environment_path, state=state, problems=tuple(problems))


def _clear_read_only_and_retry(function: Callable[[str], None], error_path: str, _error: object) -> None:
    """Clear the read-only bit and retry one deletion, as Windows needs."""
    os.chmod(error_path, stat.S_IWRITE)
    function(error_path)


def delete_tree(path: Path) -> None:
    """Delete one tree, retrying read-only files on Windows."""
    if sys.version_info >= (3, 12):
        shutil.rmtree(path, onexc=_clear_read_only_and_retry)
    else:
        shutil.rmtree(path, onerror=_clear_read_only_and_retry)


def quarantine_path(root: Path, environment: Path) -> Path:
    """Pick an unused, gitignored home for an environment that cannot be deleted."""
    stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    directory = root / ".artifacts"
    candidate = directory / f"venv-quarantine-{environment.name}-{stamp}"
    counter = 1
    while candidate.exists() or candidate.is_symlink():
        counter += 1
        candidate = directory / f"venv-quarantine-{environment.name}-{stamp}-{counter}"
    return candidate


def rename_path(source: Path, destination: Path) -> None:
    """Move a directory, as the quarantine tier needs."""
    source.rename(destination)


@dataclass(frozen=True)
class FileOperations:
    """The two file-system actions a repair needs, injectable for tests."""

    delete: Callable[[Path], None] = delete_tree
    move: Callable[[Path, Path], None] = rename_path


@dataclass(frozen=True)
class Removal:
    """How an unusable environment was cleared, or why it could not be."""

    deleted: bool = False
    quarantined: Optional[Path] = None
    blocked: Message = ""

    @property
    def cleared(self) -> bool:
        return self.blocked == ""


def remove_environment(root: Path, environment: Path, files: Optional[FileOperations] = None) -> Removal:
    """Clear an unusable environment, preferring deletion and then a move.

    Deletion is the normal path, but a read-only or in-use residue can refuse
    it: uv hardlinks a platform Python into the environment, and that link can
    inherit read-only attributes that defeat ``unlink`` even after the retry.
    Moving the whole directory aside is a metadata-only rename, so the repair
    still succeeds and the original path is free for a clean build. The moved
    directory keeps the evidence for a post-mortem.
    """
    operations = files or FileOperations()
    try:
        if environment.is_symlink() or not environment.is_dir():
            environment.unlink()
        else:
            operations.delete(environment)
        return Removal(deleted=True)
    except OSError as error:
        quarantined = quarantine_path(root, environment)
        try:
            quarantined.parent.mkdir(parents=True, exist_ok=True)
            operations.move(environment, quarantined)
        except OSError as rename_error:
            return Removal(blocked=f"{error}; moving it to '{display_path(quarantined)}' also failed: {rename_error}")
        return Removal(quarantined=quarantined)
@dataclass(frozen=True)
class RepairOutcome:
    """Before and after states plus the exact rebuild commands that ran."""

    before: EnvironmentReport
    after: EnvironmentReport
    removed: bool = False
    quarantined: Optional[Path] = None
    commands: Tuple[Tuple[str, ...], ...] = ()
    output: Message = ""

    @property
    def ok(self) -> bool:
        return not self.after.needs_repair

    @property
    def rebuilt_in_place(self) -> bool:
        return any(command[1:] == ("venv", "--allow-existing") for command in self.commands)


def run_uv(command: Sequence[str], root: Path) -> Tuple[int, str]:
    """Run uv in the project root, merging its output for the failure message."""
    try:
        completed = subprocess.run(
            list(command),
            cwd=str(root),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError as error:
        raise ValueError(
            f"could not run '{command[0]}': {error}. Install uv (https://docs.astral.sh/uv/), then re-run '{REPAIR_COMMAND}'."
        ) from None
    return completed.returncode, (completed.stdout or "") + (completed.stderr or "")


def repair_project_environment(
    root: Path,
    uv_executable: Optional[str] = None,
    environment: Optional[Mapping[str, str]] = None,
    run: Optional[Runner] = None,
    files: Optional[FileOperations] = None,
) -> RepairOutcome:
    """Rebuild an unusable environment from the committed lock file.

    A missing or healthy environment is left alone and costs no uv process:
    uv creates a missing environment on the next run, and rebuilding a healthy
    one would only slow every preflight down.

    Three tiers, each one covering a real refusal: delete the directory, move
    it aside, or, when the file system forbids both, let uv rebuild in place
    with ``uv venv --allow-existing``. uv accepts a directory that already
    holds an inert residue as long as ``pyvenv.cfg`` and an interpreter exist,
    so the last tier still yields an environment every later ``uv run`` can
    use, and the residue costs nothing beyond disk space.
    """
    before = inspect_project_environment(root, environment)
    if not before.needs_repair:
        return RepairOutcome(before=before, after=before)

    position = containment(root, before.path)
    if position.fatal:
        raise ValueError(
            f"{position.reason}, so the repair must not touch it. Point UV_PROJECT_ENVIRONMENT at a directory inside the "
            f"project, or unset it, then re-run '{REPAIR_COMMAND}'."
        )
    removal = remove_environment(root, before.path, files) if position.inside_root else Removal(blocked=position.reason)
    executable = uv_executable or shutil.which("uv") or "uv"
    rebuild: List[Tuple[str, ...]] = []
    if not removal.cleared:
        rebuild.append((executable, "venv", "--allow-existing"))
    rebuild.append((executable, "sync", "--locked"))

    invoke = run or run_uv
    outputs: List[Message] = []
    code = 0
    for command in rebuild:
        code, output = invoke(command, root)
        outputs.append(output)
        if code != 0:
            break

    after = inspect_project_environment(root, environment)
    if after.state != STATE_VALID:
        attempted = ", ".join(f"uv {' '.join(command[1:])}" for command in rebuild)
        detail = "\n".join(part.strip() for part in outputs if part.strip()) or "uv produced no output"
        raise ValueError(
            f"'{display_path(before.path)}' is still not a usable virtual environment after the rebuild, so the "
            f"harness cannot run. Re-run '{REPAIR_COMMAND}' once the uv failure below is fixed"
            + (f".\n{removal.blocked}" if not removal.cleared else ".")
            + f"\n{attempted} exited {code}: {detail}"
        )
    return RepairOutcome(
        before=before,
        after=after,
        removed=True,
        quarantined=removal.quarantined,
        commands=tuple(rebuild),
        output="\n".join(outputs),
    )


def report_failure(report: EnvironmentReport) -> None:
    """Print an invalid verdict with the evidence and the one-command fix."""
    print(
        f"error: '{display_path(report.path)}' is not a usable project virtual environment, so uv cannot run the harness.",
        file=sys.stderr,
    )
    for problem in report.problems:
        print(f"  - {problem}", file=sys.stderr)
    print(f"  fix: {REPAIR_COMMAND}" if report.repairable else "  fix: point UV_PROJECT_ENVIRONMENT at a directory inside the project, or unset it", file=sys.stderr)


def check(root: Path, as_json: bool) -> int:
    """Report the environment state without changing it."""
    report = inspect_project_environment(root)
    if as_json:
        print(json.dumps(report.to_json(), indent=2))
    elif report.state == STATE_INVALID:
        report_failure(report)
    elif report.state == STATE_MISSING:
        print(f"[uv-env] '{display_path(report.path)}' does not exist yet; uv creates it on the next run.")
    else:
        print(f"[uv-env] '{display_path(report.path)}' is a usable virtual environment.")
    return 1 if report.needs_repair else 0


def describe_repair(outcome: RepairOutcome) -> str:
    """Explain why the environment was rebuilt and how."""
    commands = " && ".join(f"uv {' '.join(command[1:])}" for command in outcome.commands)
    because = "; ".join(outcome.before.problems) or "it stopped being a usable virtual environment"
    summary = f"[uv-env] rebuilt '{display_path(outcome.after.path)}' with '{commands}' because {because}"
    if outcome.quarantined is not None:
        return summary + f" (deletion was blocked, so the old directory moved to '{display_path(outcome.quarantined)}')."
    if outcome.rebuilt_in_place:
        return summary + " in place (deletion and a move were both blocked, so its inert residue stays beside the new files)."
    return summary + "."


def repair(root: Path, uv_executable: Optional[str], as_json: bool) -> int:
    """Rebuild an unusable environment, staying silent when nothing is wrong."""
    try:
        outcome = repair_project_environment(root, uv_executable)
    except ValueError as error:
        if as_json:
            print(json.dumps({"owner": OWNER, "state": STATE_INVALID, "root": str(root), "error": str(error)}, indent=2))
        else:
            print(f"error: {error}", file=sys.stderr)
        return 1

    if as_json:
        payload = outcome.after.to_json()
        payload["repair"] = {
            "removed": outcome.removed,
            "quarantined": str(outcome.quarantined) if outcome.quarantined is not None else None,
            "commands": [list(command) for command in outcome.commands],
        }
        print(json.dumps(payload, indent=2))
    elif outcome.removed:
        print(describe_repair(outcome))
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inspect or rebuild the project uv environment.")
    action = parser.add_mutually_exclusive_group()
    action.add_argument("--check", action="store_true", help="report the state and exit 1 when it is unusable")
    action.add_argument("--repair", action="store_true", help="rebuild an unusable environment (default leaves state alone)")
    parser.add_argument("--json", action="store_true", help="print the verdict as JSON")
    parser.add_argument("--root", default=None, help="project root that holds pyproject.toml (default: this repository)")
    parser.add_argument("--uv", default=None, help="uv executable to rebuild with (default: uv on PATH)")
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    """Inspect by default; rebuild only when asked."""
    arguments = build_parser().parse_args(argv)
    root = Path(arguments.root).resolve() if arguments.root else REPO_ROOT
    if not (root / "pyproject.toml").is_file():
        print(f"error: '{root}' has no pyproject.toml, so it has no uv project environment.", file=sys.stderr)
        return 1
    if arguments.repair:
        return repair(root, arguments.uv, arguments.json)
    return check(root, arguments.json)


if __name__ == "__main__":
    sys.exit(main())
