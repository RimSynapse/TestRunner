using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimSynapse.Psychology.API;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// The skill-driven trait engine (Psychology #60): the trait-axis adjacency model, the candidate-id
    /// encoding, and the skill→trait mapping table. Deterministic — the axis math and the mapping table
    /// are pure, and the def-resolution checks validate the shipped aversion defs actually loaded.
    /// </summary>
    public static class SkillAxisCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // The reachable-degree / adjacency math over a real spectrum def (NaturalMood).
            yield return new SynapseTestCase("Core_TraitAxis_SpectrumAdjacencyWalk", () =>
            {
                var mood = DefDatabase<TraitDef>.GetNamedSilentFail("NaturalMood");
                Assert.True(mood != null, "NaturalMood trait def exists");

                var reach = TraitAxis.ReachableDegrees(mood);
                Assert.True(reach.Contains(0), "reachable degrees always include the neutral 0");
                Assert.True(reach.Count >= 3, "a spectrum has several reachable degrees");

                int? plus = TraitAxis.AdjacentPlus(mood, 0);
                int? minus = TraitAxis.AdjacentMinus(mood, 0);
                Assert.True(plus.HasValue && plus.Value > 0, "from neutral, the + move is a positive degree (Optimist-side)");
                Assert.True(minus.HasValue && minus.Value < 0, "from neutral, the - move is a negative degree (Pessimist-side)");

                // From the pessimist side, - goes deeper (Depressive) and + returns to neutral.
                int? deeper = TraitAxis.AdjacentMinus(mood, minus.Value);
                Assert.True(deeper.HasValue && deeper.Value < minus.Value, "a further - step goes to the deeper negative degree");
                int? back = TraitAxis.AdjacentPlus(mood, minus.Value);
                Assert.Equal(0f, back ?? -99, "a + step from the first negative degree returns to neutral");
                return $"reach=[{string.Join(",", reach)}] plus={plus} minus={minus}";
            });

            // Candidate-id encoding round-trips for both spectrum and single traits.
            yield return new SynapseTestCase("Core_TraitAxis_CandidateIdRoundTrip", () =>
            {
                string spec = TraitAxis.SpectrumCandidate("NaturalMood", -1);
                Assert.True(spec == "NaturalMood#-1", $"spectrum id encodes the degree (was {spec})");
                Assert.True(TraitAxis.AxisIdOf(spec) == "NaturalMood", "axis id extracts before the '#'");
                Assert.True(TraitAxis.TryParse(spec, out var ax, out int deg, out var single)
                    && ax == "NaturalMood" && deg == -1 && !single.HasValue, "spectrum id parses to axis+degree, no single flag");

                string add = TraitAxis.SingleCandidate("Bloodlust", true);
                string rem = TraitAxis.SingleCandidate("Bloodlust", false);
                Assert.True(add == "Bloodlust#+" && rem == "Bloodlust#-", $"single ids use +/- sentinels (was {add}, {rem})");
                Assert.True(TraitAxis.TryParse(add, out _, out _, out var s2) && s2 == true, "single '#+' parses as add");
                Assert.True(TraitAxis.TryParse(rem, out _, out _, out var s3) && s3 == false, "single '#-' parses as remove");
                return $"{spec}, {add}, {rem}";
            });

            // Each WorkDomain resolves to a graduated distaste SPECTRUM (no hard block) and a separate
            // terminal INCAPABLE trait that DOES disable the work type, plus a real WorkTypeDef.
            yield return new SynapseTestCase("Psychology_SkillAxisMap_AversionFamilyResolves", () =>
            {
                int chec1 = 0;
                foreach (var d in SynapseSkillAxisMap.WorkDomains)
                {
                    var distaste = DefDatabase<TraitDef>.GetNamedSilentFail(d.aversionTraitDef);
                    Assert.True(distaste != null, $"distaste trait '{d.aversionTraitDef}' is defined");
                    Assert.True(distaste.degreeDatas != null && distaste.degreeDatas.Count >= 2,
                        $"'{d.aversionTraitDef}' is a graduated spectrum (reluctant -> averse)");
                    bool distasteDisables = (distaste.disabledWorkTypes != null && distaste.disabledWorkTypes.Count > 0)
                        || distaste.disabledWorkTags != WorkTags.None;
                    Assert.False(distasteDisables, $"'{d.aversionTraitDef}' must NOT hard-disable work (graduated, not binary)");

                    var incap = DefDatabase<TraitDef>.GetNamedSilentFail(d.incapableTraitDef);
                    Assert.True(incap != null, $"incapable trait '{d.incapableTraitDef}' is defined");
                    bool incapDisables = (incap.disabledWorkTypes != null && incap.disabledWorkTypes.Count > 0)
                        || incap.disabledWorkTags != WorkTags.None;
                    Assert.True(incapDisables, $"'{d.incapableTraitDef}' disables its work type");

                    var wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(d.workTypeDef);
                    Assert.True(wt != null, $"work type '{d.workTypeDef}' exists");
                    chec1++;
                }
                Assert.True(chec1 >= 10, "the full data-driven aversion family is present");
                return $"validated {chec1} work domains";
            });

            // The unease thought def shipped and has escalating stages.
            yield return new SynapseTestCase("Psychology_SkillEngine_UneaseThoughtHasStages", () =>
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail("Synapse_Unease");
                Assert.True(def != null, "Synapse_Unease thought def exists");
                Assert.True(def.stages != null && def.stages.Count >= 2, "unease has multiple escalating stages");
                return $"stages={def.stages.Count}";
            });

            // Reinforcement (the multidimensional 2nd axis): mood vs a rolling baseline, clamped [-1,1].
            yield return new SynapseTestCase("Core_MoodBaselineReinforcement", () =>
            {
                var comp = new SynapseCorePawnComp();
                float seed = comp.UpdateMoodBaselineAndGetReinforcement(0.5f);
                Assert.Equal(0f, seed, "the first day only seeds the baseline (no reinforcement yet)");

                float up = comp.UpdateMoodBaselineAndGetReinforcement(0.65f);
                Assert.True(up > 0f, "a happier-than-baseline day yields positive reinforcement");
                float down = comp.UpdateMoodBaselineAndGetReinforcement(0.20f);
                Assert.True(down < 0f, "a worse-than-baseline day yields negative reinforcement");

                var comp2 = new SynapseCorePawnComp();
                comp2.UpdateMoodBaselineAndGetReinforcement(0.5f);
                float clamped = comp2.UpdateMoodBaselineAndGetReinforcement(1.0f);
                Assert.True(clamped <= 1.0f && clamped >= 0.99f, $"reinforcement clamps at +1 (was {clamped})");
                return $"up={up:0.00} down={down:0.00} clamped={clamped:0.00}";
            });

            // Passion scaling: Major = 1.0, and a stronger (modded) passion scales above it.
            yield return new SynapseTestCase("Psychology_SkillAxisMap_PassionScale", () =>
            {
                Assert.Equal(1.0f, SynapseSkillAxisMap.PassionScale(Passion.Major), "Major passion scales at 1.0");
                Assert.True(SynapseSkillAxisMap.IsStrongPassion(Passion.Major), "Major counts as a strong passion");
                Assert.False(SynapseSkillAxisMap.IsStrongPassion(Passion.Minor), "Minor does not");
                return "scale ok";
            });
        }
    }
}
