# Releasing com.wallstop-studios.data-visualizer

[release.yml](../../.github/workflows/release.yml) owns preparation, CI, merge, tagging, and publication. The agent procedure is
[bump-version-release](../skills/bump-version-release/SKILL.md).

The workflow builds and validates Unity archives without an editor. EditMode and PlayMode remain local release gates; see `.llm/context.md`.

## Release flow

1. Add user-facing notes under `CHANGELOG.md` `## [Unreleased]`.
2. Run Release on `main` with `operation=prepare`, select a bump or `explicit_version`, and set `dry_run=false`.
3. Preparation checks the npm repository identity, bumps the version, syncs `.llm/context.md`, rotates the changelog, and validates candidate artifacts.
4. The workflow opens or reuses `release/vX.Y.Z`, dispatches Linux and Windows CI, and waits for that exact request and commit. Failed or missing CI stops the run.
5. It merges the validated head with the repository's normal squash-merge API. Changed heads and repository merge restrictions stop the run. No policy bypass is used. When the
   repository or its enterprise forbids GitHub Actions pull requests, no release PR is created: the same CI-validated branch head is merged into `main` through the merges API, and
   the run records the notice.
6. The same workflow consumes the returned merge SHA, creates an annotated tag, validates both packages and notes, publishes npm, and creates the GitHub Release.

Publication pins its own toolchain: `setup-node` installs Node 24, then `workflow.py select-toolchain` exports `RELEASE_NODE`/`RELEASE_NPM` for the installed `node`/`npm` pair
(preferring the requested version and requiring npm >= 11.5.1) and also prefixes `GITHUB_PATH`. Every later npm call (`verify`, `publish`) names the exported executable directly.
The npm floor is re-checked in the next step, which reports the resolved paths.

No manual PR merge or second dispatch is needed. GitHub does not start workflows for `GITHUB_TOKEN` merges or pushes, so explicit CI dispatch and the merge output connect the
chain. An externally merged release PR or externally pushed `v*` tag also publishes. Fork PRs cannot trigger publication through a release branch name.

The default `dry_run=true` validates the bumped candidate without pushing, opening or merging a PR, tagging, or publishing. Publication dry runs retain validated artifacts for 14
days.

## Authentication and repository policy

<!-- release-secrets:begin -->

No custom secrets are required.

<!-- release-secrets:end -->

GitHub writes use the built-in `GITHUB_TOKEN`; npm uses OIDC. No npm token, PAT, or GitHub App is needed. Preparation needs Contents, Pull requests, and Actions write permissions.
Publication needs Contents and ID token write permissions.

Enable Settings → Actions → General → Workflow permissions → Allow GitHub Actions to create and approve pull requests if the release should leave a reviewable pull request behind.
The pipeline does not require it: when an enterprise policy forbids Actions-created pull requests, preparation keeps the pushed `release/vX.Y.Z` branch and the merge step validates
and merges that branch head after CI, so no custom token is needed either way. Allow squash merges. Repository policies must permit the bot to merge after CI passes. A required
human review or deployment approval remains a human gate; remove that requirement for release PRs if zero-touch releases are required. The workflow does not approve its own PR or
bypass policy.

## npm Trusted Publisher setup

Configure package `com.wallstop-studios.data-visualizer` on npmjs.com → package settings → Trusted Publisher → GitHub Actions.

| Field                | Value                   |
| -------------------- | ----------------------- |
| Organization or user | `Ambiguous-Interactive` |
| Repository           | `DxVisualizer`          |
| Workflow filename    | `release.yml`           |
| Environment name     | leave empty             |
| Allowed actions      | allow `npm publish`     |

The manifest must use `git+https://github.com/Ambiguous-Interactive/DxVisualizer.git`. Identity is case-sensitive. Preparation checks it before remote writes; publication checks it
before packing. The workflow uses Node 24 and verifies npm is at least 11.5.1. Prereleases use `next`; stable releases use `latest`. The publisher registration is private npm
configuration; public metadata cannot verify it. After the repository transfer, update this registration before publishing from the new owner.

## npm Trusted Publisher registration after the repository transfer

[Publish run 41898366972](https://github.com/Ambiguous-Interactive/DxVisualizer/actions/runs/41898366972) built and validated every artifact for `v0.2.0`, then the registry
answered `E404`:

```text
npm error code E404
npm error 404 Not Found - PUT https://registry.npmjs.org/com.wallstop-studios.data-visualizer
```

npm returns 404 when the OIDC identity is not the package's registered Trusted Publisher. `0.1.1` was published while the repository lived at `wallstop/DataVisualizer`; after the
transfer to the `Ambiguous-Interactive` organization, the GitHub OIDC claims name the new owner, so the registration must be updated before any new version can publish. Only the
package owner can change it: npmjs.com → the package → Settings → Trusted Publisher → GitHub Actions → Organization `Ambiguous-Interactive`, Repository `DxVisualizer`, Workflow
filename `release.yml`, Environment empty, allow `npm publish`.

The `publish-npm` action now explains a 404 this way instead of only echoing the registry text, quotes npm's own debug log (which records the OIDC exchange the registry answered),
and publishes with `--loglevel=http`. Publication also runs without `setup-node`'s `registry-url`, because the placeholder `_authToken` that option writes makes npm attempt token
authentication instead of the OIDC exchange trusted publishing needs. Once the registration names the current repository, `operation=publish` with `dry_run=false` finishes the
release: npm publication, the GitHub Release, and the uploaded archives.

## Rerun and recovery

Run Release on `main` with `operation=publish` and `dry_run=false` to resume the current package version's existing tag. The tag input can stay blank; set it to resume another
version. Set `dry_run=true` to build and retain artifacts without publication. A missing tag fails at checkout rather than silently releasing current main.

Recovery uses tooling from the workflow revision and checks out the exact source tag. This lets fixes to the build process recover old releases. Tags are never moved. A merged-PR
job rerun accepts an existing tag only if it points at the same merge commit.

If preparation failed after branch creation, rerun it with the same inputs. An identical existing branch and open PR are reused. Different content fails without force-pushing. If
preparation already merged, rerun the failed publication job or use Publish. Preparing a new release after a completed preparation would bump the version again.

npm versions are permanent. If npm succeeded before GitHub publication failed, Publish skips that existing version and finishes the release assets and notes. Both archives, their
SHA-256 checksums, and changelog notes must validate before publication. The two registries do not provide an atomic transaction.

## 0.1.1 incident and recovery

[Preparation run 39084091695](https://github.com/wallstop/DataVisualizer/actions/runs/39084091695) opened [PR #275](https://github.com/wallstop/DataVisualizer/pull/275). The old
design stopped there and needed a manual merge.

[Publication run 39084443871](https://github.com/wallstop/DataVisualizer/actions/runs/39084443871) created `v0.1.1` and validated all artifacts. npm authenticated, generated
provenance, then rejected the upload with E422: `package.json` named `Ambiguous-Interactive/DxVisualizer` while provenance named `wallstop/DataVisualizer`. The registry did not
publish `0.1.1`, and GitHub publication was skipped. The prior guide incorrectly claimed that the manifest identity already matched.

[Recovery run 39086226932](https://github.com/wallstop/DataVisualizer/actions/runs/39086226932) failed earlier: Publish received an empty tag, and the old resolver required one.
Rerunning the old tag's tooling would also keep the incorrect manifest identity.

The fixed Publish path repairs only `repository.url` in the build tree for package `com.wallstop-studios.data-visualizer` version `0.1.1`, only when the URL is the exact known
legacy value and the publishing repository is `wallstop/DataVisualizer`. Code, version, release notes, and the existing tag stay intact. Both archives contain the same repaired
metadata. Unrelated repositories, versions, and metadata mismatches fail. The repair is idempotent and is recorded in the workflow log.

Regression tests cover identity rejection before preparation, the narrow metadata repair, automatic CI/merge failure boundaries, and the complete package build chain.

## Runner toolchain incident and fix

[Run 41891926057](https://github.com/Ambiguous-Interactive/DxVisualizer/actions/runs/41891926057) stopped at "Verify npm trusted publishing toolchain" with
`npm 10.9.9 is too old; trusted publishing requires npm >= 11.5.1.` `setup-node` had installed Node 24.22.2 with npm 11.19.1, and reported that pair from its own step, yet the
publishing step resolved `node v22.23.4` and npm 10.9.9 from the runner image. The gate asked the ambient `PATH` for a toolchain instead of using the one the workflow installed, so
any image or action change that reorders `PATH` decides which npm publishes.

The fix makes the toolchain explicit. The Node.js step now exposes its resolved version, `select-toolchain` locates the matching `node`/`npm` pair in the runner tool cache (falling
back to any cached pair at or above the npm floor), and exports it as `RELEASE_NODE`/`RELEASE_NPM` so every later step and every `npm` invocation uses that pair. Selection fails
closed, naming each candidate it rejected, when no cached pair reaches npm 11.5.1, and the toolchain check reports the absolute `node` and `npm` paths it validated.

[Run 41896328507](https://github.com/Ambiguous-Interactive/DxVisualizer/actions/runs/41896328507) proved the first attempt insufficient: `select-toolchain` reported the correct
tool cache directory and wrote it to `GITHUB_PATH`, and the next step still resolved `/usr/local/bin/npm` (npm 10.9.9). PATH order is not a contract here, so the selected
executables travel as environment variables, and `verify_release.py` packs with the same npm. Tags are also created before the toolchain work: a failure then leaves an existing tag
that the documented Publish dispatch resumes, instead of a merged release with no tag.

## Runner policy incident and fix

The [release procedure](../skills/bump-version-release/SKILL.md) relied on `gh pr create` for the release PR. A probe run on this repository answered
`pull request create failed: GraphQL: GitHub Actions is not permitted to create or approve pull requests (createPullRequest)`: the enterprise policy forbids Actions-created pull
requests, and the repository setting cannot be enabled. Preparation therefore failed right after pushing `release/vX.Y.Z`, and no release could be cut.

Preparation now treats a policy refusal as expected: it records a notice and continues. `complete_release_pr.py` merges whichever head preparation published. A real release PR is
still used and squash-merged when one exists; otherwise the pushed branch head is verified against the candidate, validated by the dispatched CI request, then merged into `main`
with the merges API, and its merge commit is published. A changed head, failed CI, or rejected merge still stops the run, so the gate is unchanged.

## References

- [npm provenance requirements](https://docs.npmjs.com/generating-provenance-statements/)
- [GitHub workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow)
- [npm trusted publishers](https://docs.npmjs.com/trusted-publishers/)
