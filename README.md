# RimSynapse - TestRunner

Automated in-game test harness for the [RimSynapse](https://github.com/RimSynapse) mod suite.

**This is a development mod.** It is not a gameplay mod and should not be enabled for normal play.

## What it does

Launched normally, this mod does nothing — it registers no tools, patches nothing, and costs
only an idle `GameComponent`.

Launched with `-synapse-test`, it waits for the game to finish loading, runs its test cases
against the RimSynapse agent tool registry and game state, writes results to `Player.log`,
then shuts the game down so an automated runner sees a clean exit.

```
[SYNAPSE-TEST] PASS Registry_Initializes | 133 tools registered
[SYNAPSE-TEST] FAIL Some_Case | expected 3 factions, got 0
[SYNAPSE-TEST] SUMMARY passed=9 failed=0 skipped=0
```

Results are consumed by the RimSynapse harness — `readlog.ps1`, or the `read_player_log`
and `run_tests` tools of the [`rimsynapse-mcp`](https://github.com/RimSynapse/Repo-MCP) server.

## Running the tests

From a checkout of the [Repo-MCP](https://github.com/RimSynapse/Repo-MCP) harness:

```powershell
.\harness\build.ps1                 # build all mods, dependency-ordered
.\harness\deploy.ps1                # symlink them into RimWorld\Mods
.\harness\launch.ps1 -Test          # run with -synapse-test
.\harness\readlog.ps1               # parse and classify the results
```

`launch.ps1` rotates `Player.log` first, then stops the game once it sees the `SUMMARY`
line, so a hung run doesn't burn the full timeout.

The mod must be active in `ModsConfig.xml` and should load **last**, so that every other
RimSynapse mod has registered its tools before the tests execute.

## Writing a test case

Cases live in `Source/Cases/`. Each returns `SynapseTestCase` instances; throwing
`SynapseTestFailure` (via the `Assert` helpers) fails the case, returning normally passes it
and the returned string is logged as detail.

```csharp
yield return new SynapseTestCase("Factions_GetMotivatedFactions", () =>
{
    var json = SynapseToolRegistry.ExecuteTool("get_motivated_factions", "{}");
    Assert.NotEmpty(json, "tool should return a payload");
    var arr = JArray.Parse(json);
    Assert.True(arr.Count > 0, "expected at least one motivated faction");
    return $"{arr.Count} motivated factions";
});
```

Register the new set in `TestRunnerGameComponent.GameComponentUpdate()`.

Case names follow `<Repo>_<CaseName>` so results map back to the issue whose test plan
they satisfy. An unexpected exception is reported as a failure rather than taking the game
down, so one broken case never hides the rest.

Failures are logged with `Log.Message`, not `Log.Error` — the harness classifies lines
matching `error` as blocking build failures, and the `FAIL` token already carries the signal.

## Building

Requires the .NET SDK. `Source/GamePath.props` points at your RimWorld install.

```powershell
dotnet build Source\RimSynapseTestRunner.csproj
```

Output goes to `Assemblies\RimSynapseTestRunner.dll`. RimSynapse Core must be built first —
the project references `..\..\Core\Assemblies\RimSynapseCore.dll`.

## License

[PolyForm Noncommercial License 1.0.0](LICENSE) — free to use, modify, and redistribute for
any noncommercial purpose. Commercial/paid use is not permitted.
