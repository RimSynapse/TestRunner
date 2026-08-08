using System.Collections.Generic;
using RimSynapse.Comps;
using RimSynapse.Psychology.API;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Stage 2 of the 0.7.1 redesign: the trait engine (Core #79 + Psychology #44–#46). Multi-day
    /// pressure accumulation with decay, trait resistance, the consistency gate, and the whitelist.
    /// Deterministic — the pressure model and policy helpers are pure.
    /// </summary>
    public static class TraitEngineCases
    {
        private const long Day = 60000L;

        public static IEnumerable<SynapseTestCase> All()
        {
            // #79: pressure accumulates across days (minus daily decay) and ebbs to nothing when evidence stops.
            yield return new SynapseTestCase("Core_TraitPressureAccumulatesAndDecays", () =>
            {
                var comp = new SynapseCorePawnComp();
                // Ticks are nonzero (0 is the "never updated" sentinel; real game ticks are always large).
                float p0 = comp.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, Day);
                Assert.Equal(0.5f, p0, "first day's pressure is the raw contribution");
                // Next day: decay 0.2 (1 day) then +0.5 => 0.3 + 0.5 = 0.8
                float p1 = comp.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 2 * Day);
                Assert.True(p1 > 0.79f && p1 < 0.81f, $"day-2 pressure should be ~0.8 (was {p1})");
                comp.TryGetTraitPressure("Bloodlust", out var tp);
                Assert.True(tp.peak >= p1, "peak tracks the highest pressure");

                // No evidence for 5 days -> decays to 0 and the entry is dropped.
                comp.DecayTraitPressuresToZero(2 * Day + 5 * Day);
                Assert.False(comp.TryGetTraitPressure("Bloodlust", out _), "stale pressure decays to zero and is removed");
                return $"p0={p0}, p1={p1:0.00}";
            });

            // #44: resistance slows accumulation (pressure += dailyPressure x (1 - resistance)).
            yield return new SynapseTestCase("Core_TraitResistanceSlowsAccumulation", () =>
            {
                var comp = new SynapseCorePawnComp();
                float unresisted = comp.AccumulateTraitPressure("Kind", 0.5f, "remove", 0f, 0L);
                float resisted = comp.AccumulateTraitPressure("Nerves", 0.5f, "remove", 0.8f, 0L);
                Assert.True(resisted < unresisted, "a resistant trait accumulates pressure more slowly");
                Assert.True(resisted > 0.09f && resisted < 0.11f, $"0.5 x (1-0.8) ~= 0.1 (was {resisted})");
                return $"unresisted={unresisted}, resisted={resisted:0.00}";
            });

            // #45: the consistency gate floors today's evidence when the model says unlikely.
            yield return new SynapseTestCase("Psychology_ConsistencyGateFloorsUnlikely", () =>
            {
                Assert.Equal(0f, SynapseTraitPolicy.GateDailyPressure("none", 0.9f), "none => 0");
                Assert.Equal(0f, SynapseTraitPolicy.GateDailyPressure("low", 0.9f), "low => 0");
                Assert.Equal(0.9f, SynapseTraitPolicy.GateDailyPressure("high", 0.9f), "high passes through");
                Assert.Equal(0.5f, SynapseTraitPolicy.GateDailyPressure("moderate", 0.5f), "moderate passes through");
                return "gate ok";
            });

            // #46: the whitelist blocks dangerous/identity traits; #44: protected traits are flagged.
            yield return new SynapseTestCase("Psychology_TraitPolicyWhitelistAndProtection", () =>
            {
                Assert.True(SynapseTraitPolicy.IsWhitelisted("Bloodlust"), "Bloodlust is AI-modifiable");
                Assert.False(SynapseTraitPolicy.IsWhitelisted("Psychopath"), "Psychopath can never be AI-added");
                Assert.True(SynapseTraitPolicy.IsProtected("Nerves"), "Nerves (nerves of steel) is protected");
                Assert.True(SynapseTraitPolicy.ResistanceFactor("Nerves") > 0.5f, "Nerves strongly resists");
                Assert.False(SynapseTraitPolicy.IsProtected("Kind"), "Kind is not protected");
                return "policy ok";
            });

            // #45 end-to-end at the unit level: a single gated day never crosses; sustained days do.
            yield return new SynapseTestCase("Core_SustainedPressureCrossesButOneDayDoesNot", () =>
            {
                // One gated "unlikely" day contributes nothing.
                var lone = new SynapseCorePawnComp();
                float gated = SynapseTraitPolicy.GateDailyPressure("low", 0.9f);
                float p = lone.AccumulateTraitPressure("Bloodlust", gated, "add", 0f, Day);
                Assert.True(p < 1.0f, "a single 'unlikely' day cannot reach the shift threshold");

                // Three sustained real days do cross 1.0 (0.5 -> 0.8 -> 1.1).
                var streak = new SynapseCorePawnComp();
                streak.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, Day);
                streak.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 2 * Day);
                float p3 = streak.AccumulateTraitPressure("Bloodlust", 0.5f, "add", 0f, 3 * Day);
                Assert.True(p3 >= 1.0f, $"sustained multi-day evidence crosses the threshold (was {p3:0.00})");
                return $"gatedDay={p:0.00}, day3={p3:0.00}";
            });
        }
    }
}
