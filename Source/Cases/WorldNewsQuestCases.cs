using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Quests;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers quest-outcome news (WorldNews#14): a resolved quest becomes an event about the factions
    /// whose territory it happened in. The pure <see cref="QuestNewsReactor.BuildQuestEvent"/> rules are
    /// tested against synthetic region snapshots (no world, no live quest), and a structural check
    /// confirms the Harmony hook on <c>Quest.End</c> is actually bound so the live path can fire.
    /// </summary>
    public static class WorldNewsQuestCases
    {
        private static ProvinceSnapshot OwnedRegion(bool contested)
        {
            var snap = new ProvinceSnapshot
            {
                provinceId = 42, regionName = "Thornmarch", contested = contested,
                primaryOwnerId = "holder", primaryOwnerName = "the Marsh Wardens",
            };
            if (contested)
            {
                snap.contenderIds.AddRange(new[] { "holder", "rival" });
                snap.contenderNames.AddRange(new[] { "the Marsh Wardens", "the Dune Reavers" });
            }
            return snap;
        }

        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("WorldNews_QuestResolutionEmitsEvent", () =>
            {
                var e = QuestNewsReactor.BuildQuestEvent(OwnedRegion(contested: true), "Break the Siege", QuestEndOutcome.Success, 1000);
                Assert.NotNull(e, "a quest resolving in a claimed, contested region should emit an event");
                Assert.Equal(WorldNewsEventKind.QuestOutcome, e.kind, "wrong event kind");
                Assert.Equal("holder", e.factionAId, "the territory holder should be the primary actor (derived from territory)");
                Assert.Equal("rival", e.factionBId, "a contested region's rival should be the second actor");
                Assert.Equal("succeeded", e.qualifier, "a Success outcome should read as 'succeeded'");
                Assert.Contains(e.ToNewsLine(), "the Marsh Wardens", "the news line must name the territory holder");
                Assert.Contains(e.ToNewsLine(), "the Dune Reavers", "the news line must name the reacting rival");

                // The affected set is territory-derived: an uncontested region still emits, naming just the holder.
                var solo = QuestNewsReactor.BuildQuestEvent(OwnedRegion(contested: false), "Escort", QuestEndOutcome.Fail, 1000);
                Assert.NotNull(solo, "a quest in a singly-held region should still emit");
                Assert.True(solo.factionBId == null, "an uncontested region should name only the holder");
                Assert.Contains(solo.ToNewsLine(), "came to nothing", "a Fail outcome should read as 'came to nothing'");

                return "quest outcome emits an event named from territory, holder + rival on a contested frontier";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A quest resolves success/fail in a claimed region",
            expectation: "One quest-outcome event whose actors are the region's holders, not the quest giver");

            yield return new SynapseTestCase("WorldNews_QuestOutsideTerritoryIsQuiet", () =>
            {
                // Unclaimed region (no primary owner) → nothing.
                var unclaimed = new ProvinceSnapshot { provinceId = 7, regionName = "The Barrens", primaryOwnerId = null };
                Assert.True(QuestNewsReactor.BuildQuestEvent(unclaimed, "Raid", QuestEndOutcome.Success, 0) == null,
                    "a quest in unclaimed territory must produce nothing");

                // No region resolved at all → nothing, no throw.
                Assert.True(QuestNewsReactor.BuildQuestEvent(null, "Raid", QuestEndOutcome.Success, 0) == null,
                    "a null region snapshot must produce nothing");

                // A non-reported outcome (Unknown) on a claimed region → nothing.
                Assert.True(QuestNewsReactor.BuildQuestEvent(OwnedRegion(false), "Raid", QuestEndOutcome.Unknown, 0) == null,
                    "an Unknown outcome is not news, even on claimed ground");

                return "unclaimed territory, missing region, and non-final outcomes all stay quiet without error";
            },
            tier: "Execution", polarity: "negative",
            scenario: "A quest resolves outside any claimed region, or with a non-final outcome",
            expectation: "No event and no exception");

            yield return new SynapseTestCase("WorldNews_QuestHookIsBound", () =>
            {
                var target = AccessTools.Method(typeof(Quest), nameof(Quest.End));
                Assert.NotNull(target, "Quest.End could not be resolved — the resolution point moved");

                var info = Harmony.GetPatchInfo(target);
                Assert.NotNull(info, "Quest.End has no Harmony patches at all — WorldNews's hook is not applied");

                bool ours = false;
                if (info.Postfixes != null)
                {
                    foreach (var p in info.Postfixes)
                    {
                        if (p != null && p.owner == "rimsynapse.worldnews") { ours = true; break; }
                    }
                }
                Assert.True(ours, "no WorldNews postfix found on Quest.End — the quest-outcome hook is not bound");

                return "WorldNews's postfix is bound to Quest.End";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The mod's Harmony patch on the quest resolution point",
            expectation: "A WorldNews postfix is present on Quest.End so the live quest-news path can fire");
        }
    }
}
