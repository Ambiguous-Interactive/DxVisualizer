#!/usr/bin/env python3
"""Project virtual-environment contracts (issues #509, #511).

``uv`` refuses to use a project environment that exists but is not a virtual
environment. An interrupted first ``uv run`` leaves a Windows-layout
``.venv/Scripts/python.exe`` behind without ``pyvenv.cfg``, and every later
``uv run`` then dies before Python starts:

    failed to locate pyvenv.cfg: The system cannot find the file specified.

The failure is opaque because it names a file the user never created and it
stops every npm script at once. These cases pin the inspector that names the
residue, the repair that rebuilds the environment from the committed lock
file, and the npm preflight that keeps every uv entry point self-healing.
Each case builds its own temp root and injects a fake uv runner, so no case
touches a real environment or the network.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Callable, Dict, List, Mapping, Optional, Sequence, Tuple

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from _test_helpers import assert_true, new_temp_root, remove_temp_root, run_cases, write_fixture_file, find_python
import uv_environment

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
SCRIPT = REPO_ROOT / "scripts" / "uv_environment.py"
PACKAGE_JSON = REPO_ROOT / "package.json"
PREFLIGHT = "uv run --no-project scripts/uv_environment.py --repair"
RESIDUE = ".partial-bootstrap"
GUIDANCE = "npm run env:repair"


def native_home() -> str:
    """Return a `home` value the current platform's uv writes."""
    return "C:\\Python312" if sys.platform.startswith("win") else "/usr/bin"


def foreign_home() -> str:
    """Return a `home` value only another operating system writes."""
    return "/usr/bin" if sys.platform.startswith("win") else "C:\\Python312"


def write_valid_environment(root: Path, relative: str = ".venv") -> Path:
    """Build the marker set uv accepts: pyvenv.cfg plus a runnable interpreter."""
    environment = root / relative
    if sys.platform.startswith("win"):
        interpreter = environment / "Scripts" / "python.exe"
    else:
        interpreter = environment / "bin" / "python"
    interpreter.parent.mkdir(parents=True, exist_ok=True)
    interpreter.write_text("", encoding="utf-8")
    if not sys.platform.startswith("win"):
        interpreter.chmod(0o755)
    (environment / "pyvenv.cfg").write_text(f"home = {native_home()}\n", encoding="utf-8")
    return environment


def write_interrupted_environment(root: Path, relative: str = ".venv") -> Path:
    """Reproduce the host residue: a copied launcher, no pyvenv.cfg.

    RESIDUE names the partial-bootstrap evidence. A copied launcher alone is
    the residue on Windows but a normal name in a finished environment there,
    so the assertions track the marker instead of the platform filename.
    """
    environment = root / relative
    launcher = environment / "Scripts" / "python.exe"
    launcher.parent.mkdir(parents=True, exist_ok=True)
    launcher.write_text("", encoding="utf-8")
    (environment / ".lock").write_text("", encoding="utf-8")
    (environment / RESIDUE).write_text("", encoding="utf-8")
    return environment


def residue(environment: Path) -> Path:
    """Return the partial-bootstrap marker path inside an environment."""
    return environment / RESIDUE


def new_project_root(prefix: str) -> Path:
    """Build a temp project root the CLI accepts: a manifest beside the environment."""
    root = new_temp_root(prefix=prefix)
    write_fixture_file(root, "pyproject.toml", '[project]\nname = "uv-environment-fixture"\nversion = "0.0.0"\n')
    return root


def stub_runner(*, build: bool = True, exit_code: int = 0, output: str = "", build_at: Optional[Path] = None) -> Tuple[List[Sequence[str]], Callable[[Sequence[str], Path], Tuple[int, str]]]:
    """Return a fake uv runner and the call log it appends to."""
    calls: List[Sequence[str]] = []

    def run(command: Sequence[str], cwd: Path) -> Tuple[int, str]:
        calls.append(list(command))
        if build and exit_code == 0:
            if build_at is None:
                write_valid_environment(cwd)
            else:
                write_valid_environment(build_at.parent, build_at.name)
        return exit_code, output

    return calls, run


def inspect_project(root: Path, environment: Optional[Mapping[str, str]] = None) -> uv_environment.EnvironmentReport:
    """Inspect with a pinned environment map so ambient variables cannot leak in."""
    return uv_environment.inspect_project_environment(root, {} if environment is None else environment)


def repair_project(
    root: Path,
    run: Optional[Callable[[Sequence[str], Path], Tuple[int, str]]] = None,
    environment: Optional[Mapping[str, str]] = None,
    files: Optional[uv_environment.FileOperations] = None,
) -> uv_environment.RepairOutcome:
    """Repair with a pinned environment map so ambient variables cannot leak in."""
    return uv_environment.repair_project_environment(root, uv_executable="uv", environment={} if environment is None else environment, run=run, files=files)


def cli(arguments: Sequence[str]) -> Tuple[str, int]:
    """Run the real script as a child so exit codes stay observable.

    The ambient ``UV_PROJECT_ENVIRONMENT`` is stripped: the devcontainer sets
    it to a shared container path, and a fixture root must resolve its own
    environment or every CLI case would inspect that shared path instead.
    """
    environment = {name: value for name, value in os.environ.items() if name != "UV_PROJECT_ENVIRONMENT"}
    completed = subprocess.run(
        [find_python(), str(SCRIPT), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=environment,
        check=False,
    )
    return ((completed.stdout or "") + (completed.stderr or "")), completed.returncode


def case_missing_environment_is_healthy() -> None:
    root = new_temp_root(prefix="uv-env-missing-")
    try:
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_MISSING, f"expected missing, got {report.state}")
        assert_true(not report.needs_repair, "a missing environment is uv's job to create, not a failure")
        assert_true(report.problems == (), f"expected no problems, got {report.problems}")
    finally:
        remove_temp_root(root)


def case_interrupted_windows_residue_is_invalid() -> None:
    root = new_temp_root(prefix="uv-env-interrupted-")
    try:
        environment = write_interrupted_environment(root)
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_INVALID, f"expected invalid, got {report.state}")
        assert_true(report.needs_repair, "an interrupted environment must request repair")
        assert_true(report.path == environment, f"expected {environment}, got {report.path}")
        assert_true(any("pyvenv.cfg" in problem for problem in report.problems), f"expected a pyvenv.cfg problem, got {report.problems}")
        reports_interpreter = any("interpreter" in problem for problem in report.problems)
        expected_interpreter = not sys.platform.startswith("win")
        assert_true(reports_interpreter == expected_interpreter, f"interpreter problem reporting must match the platform layout, got {report.problems}")
    finally:
        remove_temp_root(root)


def case_pyvenv_without_interpreter_is_invalid() -> None:
    root = new_temp_root(prefix="uv-env-nointerp-")
    try:
        environment = root / ".venv"
        environment.mkdir()
        (environment / "pyvenv.cfg").write_text(f"home = {native_home()}\n", encoding="utf-8")
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_INVALID, f"expected invalid, got {report.state}")
        assert_true(any("interpreter" in problem for problem in report.problems), f"expected an interpreter problem, got {report.problems}")
        assert_true(not any("pyvenv.cfg" in problem for problem in report.problems), f"pyvenv.cfg is present, got {report.problems}")
    finally:
        remove_temp_root(root)


def case_environment_path_that_is_a_file_is_invalid() -> None:
    root = new_temp_root(prefix="uv-env-file-")
    try:
        write_fixture_file(root, ".venv", "not a directory\n")
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_INVALID, f"expected invalid, got {report.state}")
        assert_true(any("not a directory" in problem for problem in report.problems), f"expected a directory problem, got {report.problems}")
    finally:
        remove_temp_root(root)


def case_valid_environment_is_healthy() -> None:
    root = new_temp_root(prefix="uv-env-valid-")
    try:
        write_valid_environment(root)
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_VALID, f"expected valid, got {report.state} ({report.problems})")
        assert_true(not report.needs_repair, "a valid environment must not request repair")
    finally:
        remove_temp_root(root)


def case_dangling_interpreter_link_is_invalid() -> None:
    if sys.platform.startswith("win"):
        # Windows developer mode may forbid symlinks; the marker check is shared
        # with the missing-interpreter case, so the pin stays covered there.
        return
    root = new_temp_root(prefix="uv-env-dangling-")
    try:
        environment = root / ".venv"
        (environment / "bin").mkdir(parents=True)
        (environment / "bin" / "python").symlink_to(root / "missing-python")
        (environment / "pyvenv.cfg").write_text(f"home = {native_home()}\n", encoding="utf-8")
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_INVALID, f"expected invalid, got {report.state}")
        assert_true(any("interpreter" in problem for problem in report.problems), f"expected an interpreter problem, got {report.problems}")
    finally:
        remove_temp_root(root)


def case_project_environment_override_is_inspected() -> None:
    root = new_temp_root(prefix="uv-env-override-")
    try:
        write_valid_environment(root, "custom-env")
        environment = {"UV_PROJECT_ENVIRONMENT": "custom-env"}
        report = inspect_project(root, environment)
        assert_true(report.state == uv_environment.STATE_VALID, f"expected the override inspected, got {report.state}")
        assert_true(report.path == root / "custom-env", f"expected the override path, got {report.path}")
        write_interrupted_environment(root, "other-env")
        other = inspect_project(root, {"UV_PROJECT_ENVIRONMENT": str(root / "other-env")})
        assert_true(other.state == uv_environment.STATE_INVALID, f"expected the absolute override inspected, got {other.state}")
    finally:
        remove_temp_root(root)


def case_repair_rebuilds_an_invalid_environment() -> None:
    root = new_temp_root(prefix="uv-env-repair-")
    try:
        write_interrupted_environment(root)
        calls, run = stub_runner()
        outcome = repair_project(root, run=run)
        assert_true(outcome.removed, "the broken directory must be removed")
        assert_true(outcome.ok, f"expected a healthy environment, got {outcome.after.state} ({outcome.after.problems})")
        assert_true(len(calls) == 1, f"expected one uv invocation, got {calls}")
        assert_true(Path(calls[0][0]) == Path("uv"), f"expected the uv executable first, got {calls[0]}")
        assert_true(tuple(calls[0][1:]) == ("sync", "--locked"), f"expected the lock-faithful sync, got {calls[0]}")
        assert_true(not residue(root / ".venv").exists(), "the interrupted residue must not survive the repair")
    finally:
        remove_temp_root(root)


def case_repair_skips_a_healthy_environment() -> None:
    root = new_temp_root(prefix="uv-env-skip-")
    try:
        write_valid_environment(root)
        calls, run = stub_runner()
        outcome = repair_project(root, run=run)
        assert_true(not outcome.removed, "a valid environment must not be removed")
        assert_true(outcome.ok, "a valid environment is already repaired")
        assert_true(calls == [], f"expected no uv invocation, got {calls}")
    finally:
        remove_temp_root(root)


def case_repair_skips_a_missing_environment() -> None:
    root = new_temp_root(prefix="uv-env-absent-")
    try:
        calls, run = stub_runner()
        outcome = repair_project(root, run=run)
        assert_true(not outcome.removed, "nothing exists to remove")
        assert_true(not calls, f"uv creates a missing environment on the next run, got {calls}")
    finally:
        remove_temp_root(root)


def case_repair_fails_loudly_when_uv_leaves_the_environment_invalid() -> None:
    root = new_temp_root(prefix="uv-env-fail-")
    try:
        write_interrupted_environment(root)
        calls, run = stub_runner(build=False)
        raised = ""
        try:
            repair_project(root, run=run)
        except ValueError as error:
            raised = str(error)
        assert_true(raised != "", "a repair that leaves the environment invalid must fail, not report success")
        assert_true(GUIDANCE in raised, f"the failure must name the manual recovery, got: {raised}")
        assert_true(len(calls) == 1, f"expected one uv invocation, got {calls}")
    finally:
        remove_temp_root(root)


def case_repair_reports_uv_output_when_sync_fails() -> None:
    root = new_temp_root(prefix="uv-env-syncfail-")
    try:
        write_interrupted_environment(root)
        calls, run = stub_runner(build=False, exit_code=1, output="error: failed to fetch lock file")
        raised = ""
        try:
            repair_project(root, run=run)
        except ValueError as error:
            raised = str(error)
        assert_true([tuple(call[1:]) for call in calls] == [("sync", "--locked")], f"expected the single-tier rebuild, got {calls}")
        assert_true(raised != "", "a failing uv sync must fail the repair")
        assert_true("failed to fetch lock file" in raised, f"the failure must carry uv's output, got: {raised}")
    finally:
        remove_temp_root(root)


def case_repair_quarantines_an_environment_it_cannot_delete() -> None:
    root = new_temp_root(prefix="uv-env-quarantine-")
    try:
        environment = write_interrupted_environment(root)
        calls, run = stub_runner()

        def refuse_delete(path: Path) -> None:
            raise OSError(1, "Operation not permitted")

        files = uv_environment.FileOperations(delete=refuse_delete)
        outcome = repair_project(root, run=run, files=files)
        assert_true(outcome.removed, "the unusable environment must be dealt with")
        quarantined = outcome.quarantined
        assert_true(quarantined is not None, "an environment that cannot be deleted must be moved aside")
        assert_true(quarantined is not None and quarantined.is_dir(), f"expected the moved directory to exist, got {quarantined}")
        assert_true(quarantined is not None and residue(quarantined).is_file(), "the residue must survive in quarantine for a post-mortem")
        assert_true(quarantined is not None and ".artifacts" in quarantined.parts, f"quarantine must land in the gitignored artifacts tree, got {quarantined}")
        assert_true(not residue(environment).exists(), "the environment path must hold only the rebuilt environment")
        assert_true(not outcome.rebuilt_in_place, "a successful move leaves nothing to rebuild around")
        assert_true(outcome.ok, f"expected a healthy environment, got {outcome.after.state} ({outcome.after.problems})")
        assert_true([tuple(call[1:]) for call in calls] == [("sync", "--locked")], f"expected one lock-faithful sync, got {calls}")
    finally:
        remove_temp_root(root)


def case_repair_rebuilds_in_place_when_deletion_and_move_are_blocked() -> None:
    root = new_temp_root(prefix="uv-env-inplace-")
    try:
        environment = write_interrupted_environment(root)
        calls, run = stub_runner()

        def refuse_delete(path: Path) -> None:
            raise OSError(1, "Operation not permitted")

        def refuse_move(source: Path, destination: Path) -> None:
            raise OSError(13, "Permission denied")

        files = uv_environment.FileOperations(delete=refuse_delete, move=refuse_move)
        outcome = repair_project(root, run=run, files=files)
        assert_true(outcome.removed, "the unusable environment must be dealt with")
        assert_true(outcome.quarantined is None, "a blocked move leaves nothing quarantined")
        assert_true(outcome.rebuilt_in_place, "the last tier must rebuild around the residue")
        invoked = [tuple(call[1:]) for call in calls]
        assert_true(invoked == [("venv", "--allow-existing"), ("sync", "--locked")], f"expected the in-place ladder, got {invoked}")
        assert_true(residue(environment).is_file(), "the inert residue is expected to stay in the last tier")
        assert_true(outcome.ok, f"expected a usable environment, got {outcome.after.state} ({outcome.after.problems})")
        assert_true("in place" in uv_environment.describe_repair(outcome), f"expected the in-place explanation, got: {uv_environment.describe_repair(outcome)}")
    finally:
        remove_temp_root(root)


def case_environment_pointing_at_the_project_root_is_refused() -> None:
    root = new_project_root(prefix="uv-env-root-")
    try:
        environment = {"UV_PROJECT_ENVIRONMENT": "."}
        report = inspect_project(root, environment)
        assert_true(report.state == uv_environment.STATE_INVALID, f"the project root is never an environment, got {report.state}")
        assert_true(not report.repairable, "a fatal path must not advertise an automatic repair")
        calls, run = stub_runner()
        raised = ""
        try:
            repair_project(root, environment=environment, run=run)
        except ValueError as error:
            raised = str(error)
        assert_true(raised != "", "a repair must refuse the project root")
        assert_true("UV_PROJECT_ENVIRONMENT" in raised, f"the refusal must name the variable to fix, got: {raised}")
        assert_true(calls == [], f"no uv command may run against a fatal path, got {calls}")
        assert_true((root / "pyproject.toml").is_file(), "the project root must survive untouched")
        assert_true(not (root / "pyvenv.cfg").exists(), "nothing may be written into the project root")
    finally:
        remove_temp_root(root)


def case_environment_pointing_at_an_ancestor_is_refused() -> None:
    parent = new_temp_root(prefix="uv-env-ancestor-")
    try:
        root = parent / "project"
        root.mkdir()
        write_fixture_file(root, "pyproject.toml", '[project]\nname = "uv-environment-fixture"\nversion = "0.0.0"\n')
        environment = {"UV_PROJECT_ENVIRONMENT": ".."}
        report = inspect_project(root, environment)
        assert_true(report.state == uv_environment.STATE_INVALID, f"a parent of the project is never an environment, got {report.state}")
        raised = ""
        try:
            repair_project(root, environment=environment, run=stub_runner()[1])
        except ValueError as error:
            raised = str(error)
        assert_true(raised != "", "a repair must refuse a parent of the project")
        assert_true((root / "pyproject.toml").is_file(), "the project must survive untouched")
    finally:
        remove_temp_root(parent)


def case_environment_outside_the_project_is_rebuilt_but_never_deleted() -> None:
    root = new_project_root(prefix="uv-env-outside-project-")
    outside = new_temp_root(prefix="uv-env-outside-")
    try:
        write_interrupted_environment(outside)
        environment = {"UV_PROJECT_ENVIRONMENT": str(outside / ".venv")}
        report = inspect_project(root, environment)
        assert_true(report.state == uv_environment.STATE_INVALID, f"the outside environment is broken, got {report.state}")
        assert_true(report.repairable, "an outside environment is still repairable, only never deletable")
        calls, run = stub_runner(build_at=outside / ".venv")
        outcome = repair_project(root, environment=environment, run=run)
        assert_true(outcome.quarantined is None, "an outside environment must never be moved")
        invoked = [tuple(call[1:]) for call in calls]
        assert_true(invoked == [("venv", "--allow-existing"), ("sync", "--locked")], f"expected the in-place ladder, got {invoked}")
        assert_true(residue(outside / ".venv").is_file(), "an outside environment keeps its residue instead of being deleted")
        assert_true(outcome.ok, f"expected a usable environment, got {outcome.after.state} ({outcome.after.problems})")
    finally:
        remove_temp_root(root)
        remove_temp_root(outside)


def case_environment_with_an_unresolvable_path_is_refused() -> None:
    if sys.platform.startswith("win"):
        # Creating a symlink loop needs privileges Windows CI may not grant.
        return
    root = new_project_root(prefix="uv-env-loop-")
    try:
        (root / "loop").symlink_to("loop")
        environment = {"UV_PROJECT_ENVIRONMENT": "loop"}
        report = inspect_project(root, environment)
        assert_true(report.state == uv_environment.STATE_INVALID, f"an unresolvable path is never an environment, got {report.state}")
        assert_true(not report.repairable, "an unverified path must not advertise an automatic repair")
        calls, run = stub_runner()
        raised = ""
        try:
            repair_project(root, environment=environment, run=run)
        except ValueError as error:
            raised = str(error)
        assert_true(raised != "", "an unresolvable environment path must fail the repair, not escape as another error")
        assert_true(calls == [], f"no uv command may run against a fatal path, got {calls}")
        assert_true((root / "loop").is_symlink(), "the unresolvable path must survive untouched")
    finally:
        remove_temp_root(root)


def case_environment_from_a_dot_dot_path_is_refused() -> None:
    root = new_project_root(prefix="uv-env-dotdot-")
    try:
        environment = {"UV_PROJECT_ENVIRONMENT": "missing/.."}
        report = inspect_project(root, environment)
        assert_true(report.state == uv_environment.STATE_INVALID, f"'missing/..' names the project root, got {report.state}")
        assert_true(not report.repairable, "a path that normalizes to the project root must not be repaired")
        calls, run = stub_runner()
        raised = ""
        try:
            repair_project(root, environment=environment, run=run)
        except ValueError as error:
            raised = str(error)
        assert_true(raised != "", "a path that normalizes to the project root must fail the repair")
        assert_true(calls == [], f"no uv command may run against a fatal path, got {calls}")
        assert_true((root / "pyproject.toml").is_file(), "the project root must survive untouched")
    finally:
        remove_temp_root(root)


def case_dangling_environment_link_keeps_its_outside_target() -> None:
    if sys.platform.startswith("win"):
        # A file symlink needs privileges Windows CI may not grant; the POSIX
        # case pins the same classification the dangling-link logic provides.
        return
    root = new_project_root(prefix="uv-env-dangling-target-")
    outside = new_temp_root(prefix="uv-env-dangling-outside-")
    try:
        target = outside / "env"
        (root / ".venv").symlink_to(target, target_is_directory=True)
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_INVALID, f"a dangling link is not a usable environment, got {report.state}")
        assert_true(report.repairable, "the target can still be rebuilt")
        calls, run = stub_runner(build_at=target)
        outcome = repair_project(root, run=run)
        assert_true(outcome.quarantined is None, "a link outside the project must never be moved")
        invoked = [tuple(call[1:]) for call in calls]
        assert_true(invoked == [("venv", "--allow-existing"), ("sync", "--locked")], f"expected the in-place ladder, got {invoked}")
        assert_true((root / ".venv").is_symlink(), "the user's link must survive the repair")
        assert_true(target.is_dir(), "the rebuild must land on the link target")
        assert_true(outcome.ok, f"expected a usable environment, got {outcome.after.state} ({outcome.after.problems})")
    finally:
        remove_temp_root(root)
        remove_temp_root(outside)


def case_environment_built_by_another_operating_system_is_invalid() -> None:
    root = new_temp_root(prefix="uv-env-foreign-")
    try:
        environment = write_valid_environment(root)
        (environment / "pyvenv.cfg").write_text(f"home = {foreign_home()}\nversion_info = 3.12\n", encoding="utf-8")
        report = inspect_project(root)
        assert_true(report.state == uv_environment.STATE_INVALID, f"a foreign platform cannot run here, got {report.state} (markers all pass)")
        assert_true(any("another operating system" in problem for problem in report.problems), f"expected the other platform named, got {report.problems}")
        calls, run = stub_runner()
        outcome = repair_project(root, run=run)
        assert_true(outcome.ok, f"expected a rebuilt environment, got {outcome.after.state} ({outcome.after.problems})")
        assert_true("another operating system" in uv_environment.describe_repair(outcome), f"the repair must say why, got: {uv_environment.describe_repair(outcome)}")
        assert_true(len(calls) == 1, f"expected one uv invocation, got {calls}")
    finally:
        remove_temp_root(root)


def case_devcontainer_keeps_its_environment_outside_the_checkout() -> None:
    spec = (REPO_ROOT / ".devcontainer" / "devcontainer.json").read_text(encoding="utf-8")
    match = re.search(r'"UV_PROJECT_ENVIRONMENT"\s*:\s*"([^"]+)"', spec)
    assert_true(match is not None, "the devcontainer must keep its own environment; the bind-mounted .venv belongs to the host")
    configured = match.group(1) if match is not None else ""
    assert_true(
        "${containerWorkspaceFolder}" not in configured and not configured.startswith("./"),
        f"the container environment must live outside the shared checkout, got: {configured}",
    )
    assert_true(configured.startswith("/"), f"the container environment must be an absolute container path, got: {configured}")


def case_cli_check_fails_with_recovery_guidance() -> None:
    root = new_project_root(prefix="uv-env-cli-")
    try:
        write_interrupted_environment(root)
        output, code = cli(["--check", "--root", str(root)])
        assert_true(code == 1, f"expected exit 1 for an invalid environment, got {code}: {output}")
        assert_true("pyvenv.cfg" in output, f"expected the missing marker named, got: {output}")
        assert_true(GUIDANCE in output, f"expected the recovery command, got: {output}")
    finally:
        remove_temp_root(root)


def case_cli_check_accepts_healthy_and_absent_environments() -> None:
    root = new_project_root(prefix="uv-env-cli-ok-")
    try:
        output, code = cli(["--check", "--root", str(root)])
        assert_true(code == 0, f"a missing environment is not a failure, got {code}: {output}")
        write_valid_environment(root)
        output, code = cli(["--check", "--root", str(root)])
        assert_true(code == 0, f"a valid environment must pass, got {code}: {output}")
    finally:
        remove_temp_root(root)


def case_cli_json_reports_the_environment_state() -> None:
    root = new_project_root(prefix="uv-env-cli-json-")
    try:
        write_interrupted_environment(root)
        output, code = cli(["--check", "--json", "--root", str(root)])
        assert_true(code == 1, f"expected exit 1, got {code}: {output}")
        payload = json.loads(output)
        assert_true(payload["state"] == uv_environment.STATE_INVALID, f"expected invalid state, got {payload['state']}")
        assert_true(payload["path"].endswith(".venv"), f"expected the environment path, got {payload['path']}")
        assert_true(isinstance(payload["problems"], list), f"expected a problem list, got {payload['problems']}")
    finally:
        remove_temp_root(root)


def npm_scripts() -> Dict[str, str]:
    manifest = json.loads(PACKAGE_JSON.read_text(encoding="utf-8"))
    scripts = manifest["scripts"]
    assert_true(isinstance(scripts, dict), "package.json must carry a scripts object")
    return {str(name): str(command) for name, command in scripts.items()}


def case_every_uv_npm_script_runs_the_preflight() -> None:
    missing: List[str] = []
    for name, command in npm_scripts().items():
        if "uv run" not in command or name in ("env:check", "env:repair"):
            continue
        if not command.startswith(PREFLIGHT + " && "):
            missing.append(f"{name}: {command}")
    assert_true(missing == [], "every npm script that reaches uv must repair the project environment first:\n  " + "\n  ".join(missing))


def case_npm_exposes_the_environment_commands() -> None:
    scripts = npm_scripts()
    for name in ("env:check", "env:repair"):
        assert_true(name in scripts, f"package.json must expose '{name}'")
        assert_true("--no-project" in scripts[name], f"'{name}' must run outside the project environment, got: {scripts[name]}")
        assert_true("scripts/uv_environment.py" in scripts[name], f"'{name}' must drive the environment script, got: {scripts[name]}")


def case_pre_commit_repairs_the_environment_first() -> None:
    hooks = (REPO_ROOT / ".pre-commit-config.yaml").read_text(encoding="utf-8")
    assert_true("id: uv-environment" in hooks, "pre-commit must repair the project environment before its uv hooks run")
    assert_true(PREFLIGHT in hooks, f"the pre-commit hook must drive the same repair: {PREFLIGHT}")
    assert_true(
        hooks.index("id: uv-environment") < hooks.index("id: lint-csharp-source-policy"),
        "the environment hook must run before every hook that reaches uv",
    )


CASES: Sequence[Tuple[str, Callable[[], None]]] = (
    ("missing environment is healthy", case_missing_environment_is_healthy),
    ("interrupted windows residue is invalid", case_interrupted_windows_residue_is_invalid),
    ("pyvenv.cfg without an interpreter is invalid", case_pyvenv_without_interpreter_is_invalid),
    ("environment path that is a file is invalid", case_environment_path_that_is_a_file_is_invalid),
    ("valid environment is healthy", case_valid_environment_is_healthy),
    ("dangling interpreter link is invalid", case_dangling_interpreter_link_is_invalid),
    ("project environment override is inspected", case_project_environment_override_is_inspected),
    ("repair rebuilds an invalid environment", case_repair_rebuilds_an_invalid_environment),
    ("repair skips a healthy environment", case_repair_skips_a_healthy_environment),
    ("repair skips a missing environment", case_repair_skips_a_missing_environment),
    ("repair fails loudly when uv leaves the environment invalid", case_repair_fails_loudly_when_uv_leaves_the_environment_invalid),
    ("repair reports uv output when sync fails", case_repair_reports_uv_output_when_sync_fails),
    ("repair quarantines an environment it cannot delete", case_repair_quarantines_an_environment_it_cannot_delete),
    ("repair rebuilds in place when deletion and a move are blocked", case_repair_rebuilds_in_place_when_deletion_and_move_are_blocked),
    ("environment pointing at the project root is refused", case_environment_pointing_at_the_project_root_is_refused),
    ("environment pointing at an ancestor is refused", case_environment_pointing_at_an_ancestor_is_refused),
    ("environment outside the project is rebuilt but never deleted", case_environment_outside_the_project_is_rebuilt_but_never_deleted),
    ("environment built by another operating system is invalid", case_environment_built_by_another_operating_system_is_invalid),
    ("devcontainer keeps its environment outside the checkout", case_devcontainer_keeps_its_environment_outside_the_checkout),
    ("environment with an unresolvable path is refused", case_environment_with_an_unresolvable_path_is_refused),
    ("environment from a dot-dot path is refused", case_environment_from_a_dot_dot_path_is_refused),
    ("dangling environment link keeps its outside target", case_dangling_environment_link_keeps_its_outside_target),
    ("cli check fails with recovery guidance", case_cli_check_fails_with_recovery_guidance),
    ("cli check accepts healthy and absent environments", case_cli_check_accepts_healthy_and_absent_environments),
    ("cli json reports the environment state", case_cli_json_reports_the_environment_state),
    ("every uv npm script runs the preflight", case_every_uv_npm_script_runs_the_preflight),
    ("npm exposes the environment commands", case_npm_exposes_the_environment_commands),
    ("pre-commit repairs the environment first", case_pre_commit_repairs_the_environment_first),
)


def main() -> int:
    failed = run_cases(CASES)
    if failed:
        print(f"\n{failed} case(s) failed", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
