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

A dead or misconfigured mod runs no failing tests, so the rest of the suite stays
green while it is broken. These cases exist because nothing else notices.

- `Core_AllModsInstantiated` — fails when a mod loads and then throws. It scans the
  log for "Error while instantiating a mod" and "Error in static constructor", so it
  **cannot** see a mod whose assembly never yielded its types: that mod's `Mod`
  subclass is never discovered, so it never fails to instantiate and neither string
  appears. It reported "every mod instantiated cleanly" while Factions was entirely
  dead. Use the two below for that case; do not widen this one.
- `Core_EveryShippedAssemblyIsLive` — every DLL a mod ships has a live assembly.
  Asks what *should* have loaded rather than whether any of what loaded broke, which
  is the distinction the case above gets wrong.
- `Core_DeclaredLoadOrderRespected` — no active mod is ordered before something it
  declares `loadAfter`. `loadAfter` is advisory: RimWorld loads `ModsConfig.xml` in
  the order written, and any tool that sorts a modlist alphabetically produces a
  broken order.
- `Core_NoUnhandledQueueCallbackErrors` — fails on any bare "Callback error" from
  the main-thread queue; deferred callbacks must carry their own try/catch.
- `Regions_AdapterProfilesMatchRealAssemblies` and
  `Regions_AdapterRulesDoNotReachAcrossMods` — the integration profiles name foreign
  members and types as strings, and a wrong name costs no error: the adapter returns
  its default and the caller reads a plausible zero. Three of four shipped profiles
  were wrong before these existed.

**Prefer structural checks to log scanning.** `Log.Messages` is a bounded buffer, so
a noisy startup can roll a log-scanning case into a false PASS. Ask ModsConfig, ask
Harmony, ask the assembly — those cannot be lost to buffer pressure. The two cases
above that do scan the log are the ones with no structural equivalent.

**A new guard that has never failed is decorative.** Prove it catches what it is for:
reintroduce the defect, watch it fail, then revert. `Regions_AdapterRulesDoNotReachAcrossMods`
was verified this way — a bare `TypeNameContains "Settlement"` rule added back to the
VFE profile made it name the two World Domination travelling parties it would claim.
