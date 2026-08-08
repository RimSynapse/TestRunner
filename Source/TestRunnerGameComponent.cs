using System;
using System.Collections.Generic;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Dev-only harness. Does nothing unless RimWorld is launched with -synapse-test.
    ///
    /// GameComponents are instantiated automatically by Verse.Game.FillComponents(), so this
    /// needs no Def and no registration. FinalizeInit() fires once the game and map are fully
    /// loaded — by then every mod's Mod ctor, [StaticConstructorOnStartup] and
    /// LongEventHandler.ExecuteWhenFinished callback has run, so the tool registry is populated.
    ///
    /// Results go to Player.log in the format readlog.ps1 parses; the game then shuts itself
    /// down so launch.ps1 sees a clean exit rather than hitting its timeout.
    /// </summary>
    public class TestRunnerGameComponent : GameComponent
    {
        private const string TestArg = "synapse-test";

        // Frames to wait after FinalizeInit before running. Gives deferred startup work
        // (LongEventHandler callbacks, comp initialisation) a chance to settle.
        private const int WarmupFrames = 30;

        // If a mod throws every frame in UIRootUpdate, Root.Shutdown() can fail to bring the
        // process down. Force an exit if we're still alive this many frames after asking nicely.
        private const int ForceExitFrames = 180;

        private bool _armed;
        private bool _completed;
        private int _framesWaited;
        private int _framesSinceShutdown = -1;

        public TestRunnerGameComponent(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            if (!GenCommandLine.CommandLineArgPassed(TestArg))
                return;

            _armed = true;
            SynapseTestReporter.Info($"TestRunner armed (-{TestArg} detected); warming up {WarmupFrames} frames.");
        }

        public override void GameComponentUpdate()
        {
            if (!_armed) return;

            // Graceful shutdown was requested but the process is still running — force it.
            if (_completed)
            {
                if (_framesSinceShutdown >= 0 && ++_framesSinceShutdown > ForceExitFrames)
                {
                    _framesSinceShutdown = -1;
                    SynapseTestReporter.Info("Shutdown did not complete; forcing process exit.");
                    Environment.Exit(0);
                }
                return;
            }

            if (_framesWaited < WarmupFrames)
            {
                _framesWaited++;
                return;
            }

            _completed = true;

            try
            {
                var cases = new List<SynapseTestCase>();
                // First: a mod killed by load order runs none of its own cases, and the rest of the
                // suite stays green while it is dead. Report that before anything else.
                cases.AddRange(LoadOrderCases.All());
                cases.AddRange(LogClassifierCases.All());
                cases.AddRange(RegistryCases.All());
                cases.AddRange(LearningHelperCases.All());
                cases.AddRange(CallbackCases.All());
                cases.AddRange(ActionExecutorCases.All());
                cases.AddRange(ScriptToolStepCases.All());
                cases.AddRange(AdaptiveTierCases.All());
                cases.AddRange(AgentBudgetCases.All());
                cases.AddRange(ToolSearchCases.All());
                cases.AddRange(AgentControlCases.All());
                cases.AddRange(PromptCases.All());
                cases.AddRange(DeadlineForecastCases.All());
                cases.AddRange(EscalationCases.All());
                cases.AddRange(ScriptValidationCases.All());
                cases.AddRange(ScriptPersistenceCases.All());
                cases.AddRange(AgentInspectorCases.All());
                cases.AddRange(PublicSurfaceCases.All());
                cases.AddRange(ActivitySummaryCases.All());
                cases.AddRange(PsychologyEvalCases.All());
                cases.AddRange(MemoryLifecycleCases.All());
                // Last, because Regions_DwellingOccupantsAreResidents spawns buildings and pawns
                // onto the live map. Nothing after it should assume an untouched colony.
                cases.AddRange(ResidencyCases.All());
                cases.AddRange(TerritorialOwnershipCases.All());
                cases.AddRange(EmpireIntegrationCases.All());
                cases.AddRange(AdapterProfileCases.All());
                cases.AddRange(AdapterScopeCases.All());
                SynapseTestReporter.Info($"Running {cases.Count} case(s).");
                SynapseTestRunner.RunAll(cases);
            }
            catch (Exception ex)
            {
                // Report rather than crash — the harness needs a parseable result either way.
                SynapseTestReporter.Fail("TestRunner_Bootstrap", $"{ex.GetType().Name}: {ex.Message}");
                SynapseTestReporter.Summary(SynapseTestRunner.Passed, SynapseTestRunner.Failed + 1, SynapseTestRunner.Skipped);
            }
            finally
            {
                _framesSinceShutdown = 0;
                Shutdown();
            }
        }

        /// <summary>
        /// Root.Shutdown() is preferred over Application.Quit(): Core patches it to run
        /// SynapseCore.Shutdown(), so this exercises the real teardown path.
        /// </summary>
        private static void Shutdown()
        {
            SynapseTestReporter.Info("Tests complete; shutting down.");
            try
            {
                Root.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Warning($"[SYNAPSE-TEST] Root.Shutdown() threw ({ex.GetType().Name}: {ex.Message}); falling back to Application.Quit().");
                UnityEngine.Application.Quit();
            }
        }
    }
}
