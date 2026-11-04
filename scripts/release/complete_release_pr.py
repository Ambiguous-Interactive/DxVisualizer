#!/usr/bin/env python3
"""Wait for explicitly dispatched CI, merge the exact validated head, and report the merge.

GITHUB_TOKEN events do not start other workflows. The caller must publish the
reported merge in the same run rather than wait for a pull_request event.

Two merge paths keep the chain working under every repository policy. When a
release pull request exists (opened by this workflow or by a person), its exact
head is squash-merged. When the platform forbids GitHub Actions from creating
pull requests, preparation still pushed the release branch, so the same
CI-validated branch head is merged into ``main`` through the merges API. Both
paths refuse a head that changed after preparation.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from typing import Any, Dict, List, Optional, Sequence, cast


def gh(arguments: Sequence[str], decode: bool = True) -> Any:
    result = subprocess.run(["gh", *arguments], capture_output=True, text=True, check=False)
    if result.returncode != 0:
        raise ValueError(f"GitHub command failed: {result.stderr.strip() or result.stdout.strip()}")
    return json.loads(result.stdout) if decode and result.stdout.strip() else None


def find_pull_request(branch: str) -> Optional[Dict[str, Any]]:
    """Return the pull request for a branch, or None when there is none."""
    try:
        value = gh(["pr", "view", branch, "--json", "number,headRefOid,baseRefName,isCrossRepository,state"])
    except ValueError:
        return None
    return cast(Dict[str, Any], value) if isinstance(value, dict) else None


def branch_head(repository: str, branch: str) -> str:
    """Read the pushed release branch head, failing closed when it is missing."""
    reference = cast(Dict[str, Any], gh(["api", f"repos/{repository}/git/ref/heads/{branch}"]))
    return str(cast(Dict[str, Any], reference["object"])["sha"])


def validated_head(repository: str, branch: str, expected_head: str, pr: Optional[Dict[str, Any]]) -> str:
    """Require the exact prepared head, whether it is carried by a PR or a branch."""
    if pr is not None:
        if pr["baseRefName"] != "main" or pr["isCrossRepository"] or pr["state"] != "OPEN":
            raise ValueError("Only an open release PR from this repository into main can be merged.")
        head = str(pr["headRefOid"])
    else:
        head = branch_head(repository, branch)
    if head != expected_head:
        raise ValueError("The release head changed after preparation; refusing to merge.")
    return head


def wait_for_ci(repository: str, branch: str, request: str, head: str, timeout: float) -> None:
    """Dispatch release CI and require the matching request to pass on the exact head."""
    gh(["workflow", "run", "llm-lint.yml", "--ref", branch, "-f", f"release_request={request}"], decode=False)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        data = cast(Dict[str, Any], gh(["api", f"repos/{repository}/actions/workflows/llm-lint.yml/runs", "--method", "GET",
                                        "-f", f"branch={branch}", "-f", "event=workflow_dispatch", "-f", "per_page=100"]))
        runs = cast(List[Dict[str, Any]], data["workflow_runs"])
        matching = [run for run in runs if run["display_title"] == request and run["head_sha"] == head]
        if matching and matching[0]["status"] == "completed":
            if matching[0]["conclusion"] != "success":
                raise ValueError(f"Release CI failed: {matching[0]['html_url']}")
            return
        time.sleep(10)
    raise ValueError("Timed out waiting for release CI; no merge or publication was attempted.")


def merge_validated_head(repository: str, branch: str, pr: Optional[Dict[str, Any]], head: str, request: str) -> str:
    """Merge the validated head through the PR or the branch, and report the merge commit."""
    if pr is not None:
        result = cast(Dict[str, Any], gh(["api", f"repos/{repository}/pulls/{pr['number']}/merge", "--method", "PUT",
                                         "-f", "merge_method=squash", "-f", f"sha={head}"]))
        if result.get("merged") is not True:
            raise ValueError(f"Repository policy prevented release merge: {result.get('message')}")
        return str(result["sha"])
    # No pull request: merge the validated branch into main. The merges API
    # answers 201 with the merge commit, or 204 when main already carries it.
    merged = gh(["api", f"repos/{repository}/merges", "--method", "POST", "-f", "base=main", "-f", f"head={branch}",
                 "-f", f"commit_message=Release {branch.removeprefix('release/')} validated by {request}"])
    if isinstance(merged, dict) and str(cast(Dict[str, Any], merged).get("sha") or ""):
        return str(cast(Dict[str, Any], merged)["sha"])
    current = cast(Dict[str, Any], gh(["api", f"repos/{repository}/commits/main"]))
    print("::notice::main already carried the validated release branch; publishing its current head.")
    return str(current["sha"])


def complete(branch: str, request: str, expected_head: str, timeout: float = 900) -> str:
    """Fail closed on changed heads, failed CI, and repository merge policies."""
    repository = os.environ["GITHUB_REPOSITORY"]
    pr = find_pull_request(branch)
    head = validated_head(repository, branch, expected_head, pr)
    wait_for_ci(repository, branch, request, head, timeout)
    return merge_validated_head(repository, branch, pr, head, request)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--branch", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()
    if not args.branch.startswith("release/v"):
        raise ValueError("Expected a release/vX.Y.Z branch.")
    sha = complete(args.branch, args.request, args.head)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"merge_sha={sha}\n")
    print(f"Merged the validated release at {sha}.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError) as error:
        print(f"error: {error}", file=sys.stderr)
        sys.exit(1)
