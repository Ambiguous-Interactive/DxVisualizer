# Unity bridge lifecycle diagnosis

Owns the bridge failure analysis in [#497](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/497). Operational commands live in the [MCP README](../mcp/README.md).

## Established repository defects

| Failure                                                             | Cause                                                                                              | Regression and correction                                                                                                   |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Clients retain an unavailable-editor catalog after Unity changes it | `read_stdout` discarded messages without an ID, including tool-list changes; HTTP GET returned 405 | Real HTTP clients initialize separate sessions and receive the CLI catalog-change notification through SSE                  |
| Doctor hides the discovery reason and calls the wrong tool          | Hard-coded `editor_status`; `isError` replaced the original content                                | Doctor selects the exact advertised status name, including `unity-cli-status`, and retains its failure text                 |
| Ctrl+C may not reach Python promptly on Windows                     | Main thread waits indefinitely in `threading.Event.wait`                                           | Main loop returns from a bounded sleep; a real console-signal test runs on Linux and Windows                                |
| Cleanup can wait for a blocked stdin writer                         | Shutdown and retire close a buffered pipe from outside its writer thread                           | Writer owns closure; stopped bridge rejects late requests; stalled-input HTTP stop regression must exit within four seconds |
| A live CLI process is mistaken for a usable Editor connection       | `/healthz.ok` describes the child process; repeat startup called it healthy                        | Explicit CLI liveness and unknown Editor readiness; doctor checks discovered tools and actual editor status                 |

The initial catalog test fails against the original bridge because initialization returns no session. The diagnostic test fails because the original doctor calls `editor_status`
instead of the available diagnostic tool. These tests are about observable transport behavior, not proof that the original host's discovery failure has the same cause.

## Host evidence and limits

On 2026-10-31, the existing Windows bridge completed HTTP health, MCP initialization and tools/list, but the CLI advertised only `unity-cli-status` and returned
`No Pipeline instance found` for the pinned `C:\Code\DataVisualizer` project. The CLI MCP version was `1.0.0-beta.15`. The user confirmed a running Editor with Pipeline installed
and a bridge terminal that did not respond to Ctrl+C. This proves a mismatch between CLI reachability and the GUI state; it does **not** identify why that CLI could not discover
Pipeline.

Do not claim the Editor is absent, reinstall Pipeline, auto-update software, kill the Editor, or silently change the project pin based on this result. Compare fresh host CLI
status/Pipeline-list output with the running bridge before attributing the failure to a cached connection, path alias, account, permissions, package load, or upstream CLI bug. A
successful canary on the original host is still required to close that part of #497.

## Recovery contract

Catalog notifications are coalesced to one pending change per active session stream, with at most 128 streams. Replacing a session's stream closes the previous stream. DELETE
releases that stream. CLI retirement closes streams and changes the session signing key, so old session requests fail with HTTP 404 and must initialize again. Anonymous health
never includes bearer tokens or CLI error payloads. Host discovery summaries print only selected instance fields and error codes, not discovery authentication tokens.

Explicit reconnect refuses pending requests. It retires only the bridge-owned CLI connection; it never replays a request or closes the Editor. Requests that timed out may still be
executing in Unity. Observe their actual status before a caller chooses whether to retry. Stop has its own authenticated HTTP control and an instance identity check, so recovery
does not depend on signal delivery through npm/uv or an interactive terminal. An old bridge needs one process replacement to load this implementation.

## Primary references

- [Unity CLI targeting and dynamic MCP catalogs](https://github.com/Unity-Technologies/unity-agent-plugin/blob/main/skills/unity-cli/references/integration-advanced.md)
- [MCP Streamable HTTP notifications and sessions](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports)
- [Python signal dispatch and threads](https://docs.python.org/3/library/signal.html#execution-of-python-signal-handlers)
- [CPython Windows interruptible-lock history](https://github.com/python/cpython/issues/74157)

Keep observed results, supported failure modes and unproven host hypotheses separate when extending these tests.
