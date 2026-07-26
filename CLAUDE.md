# RimSynapse TestRunner — Writing Cases

Dev-only mod; inert without `-synapse-test`. Cases run once the game and every other
mod have fully loaded, then the game shuts itself down so the harness sees a clean
exit. Results are parsed from `Player.log` by `Repo-MCP/harness/readlog.ps1`.

## Rules

- **Names**: `<Repo>_<CaseName>` (e.g. `Core_MutatingToolGate`) so results map back
  to the issue whose test plan they satisfy.
- **Pass/fail**: return a detail string to pass; throw `SynapseTestFailure` (via the
  `Assert` helpers) to fail. Unexpected exceptions are reported as failures without
  killing the rest of the suite.
- **Never `Log.Error`** — the harness counts it as a blocking entry. The reporter
  uses `Log.Message` for everything, including FAILs; the token carries the signal.
- **Restore what you touch.** Settings mutations go through a snapshot/restore
  wrapper (`try/finally`); see `AdaptiveTierCases.WithSettings`. Controllers with
  state expose `ResetForTesting()` — call it in the finally.
- **Test tools** are registered with a `zz_test_` prefix, idempotently (RegisterTool
  overwrites by name). Handlers must honor the registry-wide contract cases: return
  valid JSON on empty args, never throw on malformed args — the whole-registry
  cases execute *every* non-debug tool.
- **Log scanning** must skip lines carrying `[SYNAPSE-TEST]` (see
  `CallbackCases.RecentLogLines`) — otherwise a case can match its own output.
- **Be environment-defensive**: the context window may be pinned by a live provider
  discovery; probe whether it is manipulable and self-skip with a reason rather
  than assert into flakiness (see `Core_ModelSwapResets`).

## Registering a new case set

Add `Source/Cases/<Name>Cases.cs` exposing `static IEnumerable<SynapseTestCase> All()`
and append `cases.AddRange(<Name>Cases.All());` in `TestRunnerGameComponent`.
Order matters: whole-registry contract cases run early; sets that register test
tools run after them.

## Sentinel cases — do not weaken

- `Core_AllModsInstantiated` — fails when any mod dies at startup. This is the only
  test-level guard against binary-incompatible Core changes (dead mods run no
  failing tests, so the rest of the suite stays green).
- `Core_NoUnhandledQueueCallbackErrors` — fails on any bare "Callback error" from
  the main-thread queue; deferred callbacks must carry their own try/catch.
