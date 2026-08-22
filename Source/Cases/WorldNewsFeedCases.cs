using System.Collections.Generic;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers the world-map change feed (WorldNews#13): the detector rules that turn ownership
    /// snapshots into fact-events, and the bounded/deduplicated feed that stores them.
    ///
    /// <para>These are pure Tier-1/Tier-2 cases — the detector and feed hold no R&amp;T type and do no
    /// world query, so they run against synthetic snapshots on a simulated clock with no live game.
    /// That is deliberate: the issue insists events are <b>facts, not prose</b> precisely so the feed
    /// is testable without the LLM or the world, and so a rule misfiring is caught here rather than
    /// only observable end-to-end in a running colony.</para>
    /// </summary>
    public static class WorldNewsFeedCases
    {
        private static ProvinceSnapshot Prov(int id, string region, string ownerId, string ownerName, bool contested = false)
            => new ProvinceSnapshot { provinceId = id, regionName = region, primaryOwnerId = ownerId, primaryOwnerName = ownerName, contested = contested };

        public static IEnumerable<SynapseTestCase> All()
        {
            // --- Border change ---------------------------------------------------------------------
            yield return new SynapseTestCase("WorldNews_BorderChangeEmitsEvent", () =>
            {
                var before = new List<ProvinceSnapshot> { Prov(1, "Redwater Basin", "A", "the Kanou Tribe") };
                var after = new List<ProvinceSnapshot> { Prov(1, "Redwater Basin", "B", "the New Arbor Confederacy") };

                var events = WorldMapChangeDetector.DetectBorderChanges(before, after, 1000);
                Assert.Equal(1, events.Count, "a province changing owner should emit exactly one border-change event");
                Assert.Equal(WorldNewsEventKind.BorderChange, events[0].kind, "wrong event kind");
                Assert.Equal("B", events[0].factionAId, "new owner should be actor A");
                Assert.Equal("A", events[0].factionBId, "previous owner should be actor B");
                Assert.Contains(events[0].ToNewsLine(), "changed hands", "border-change news line should read as a hand-over");

                // An unchanged province must not emit; a change to unowned (null owner) must not emit.
                var noChange = WorldMapChangeDetector.DetectBorderChanges(after, after, 1000);
                Assert.Equal(0, noChange.Count, "identical snapshots must emit nothing");
                var toUnowned = WorldMapChangeDetector.DetectBorderChanges(after, new List<ProvinceSnapshot> { Prov(1, "Redwater Basin", null, null) }, 1000);
                Assert.Equal(0, toUnowned.Count, "losing a region to no one is not a border change");

                return "border-change fires on owner swap, stays silent on no-change and on becoming unowned";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A province's primary owner changes between two snapshots",
            expectation: "Exactly one border-change event naming new and previous owners; no false positives");

            // --- Dedup / cooldown ------------------------------------------------------------------
            yield return new SynapseTestCase("WorldNews_FeedDeduplicates", () =>
            {
                var feed = new WorldMapChangeFeed { DedupCooldownTicks = 1000 };
                var e1 = new WorldNewsEvent(WorldNewsEventKind.BorderChange, 0, 1, "Redwater", "B", "New Arbor", "A", "Kanou");
                var e2 = new WorldNewsEvent(WorldNewsEventKind.BorderChange, 0, 1, "Redwater", "B", "New Arbor", "A", "Kanou");

                Assert.True(feed.TryRecord(e1, 100), "first occurrence should be accepted");
                Assert.False(feed.TryRecord(e2, 500), "same change inside the cooldown must be suppressed (a flapping border is one story)");
                Assert.Equal(1, feed.Count, "feed should hold only the first of the duplicate pair");

                var e3 = new WorldNewsEvent(WorldNewsEventKind.BorderChange, 0, 1, "Redwater", "B", "New Arbor", "A", "Kanou");
                Assert.True(feed.TryRecord(e3, 100 + 1000), "the same change recurring after the cooldown is news again");
                Assert.Equal(2, feed.Count, "post-cooldown recurrence should be recorded");

                return "duplicate suppressed within cooldown, accepted again after it elapses";
            },
            tier: "Execution", polarity: "positive",
            scenario: "The same world-map change is offered twice inside the dedup window, then again after it",
            expectation: "Second offer suppressed; the post-cooldown offer accepted");

            // --- Bounded buffer eviction -----------------------------------------------------------
            yield return new SynapseTestCase("WorldNews_FeedEvictsOldestPastCap", () =>
            {
                var feed = new WorldMapChangeFeed { DedupCooldownTicks = 1 };
                int cap = WorldMapChangeFeed.MaxEvents;
                // Record cap + 5 distinct events (distinct province ids → distinct dedup keys),
                // each at a later tick so none is suppressed.
                for (int i = 0; i < cap + 5; i++)
                {
                    var e = new WorldNewsEvent(WorldNewsEventKind.SettlementFounded, 0, i, "R" + i, "F" + i, "Faction " + i);
                    Assert.True(feed.TryRecord(e, i * 10), "distinct event at a later tick should be accepted");
                }
                Assert.Equal(cap, feed.Count, "the feed must never exceed its hard cap");
                // The five oldest (province ids 0..4) should have been evicted; the newest must remain.
                Assert.Equal(5, feed.Events[0].provinceId, "oldest entries past the cap should be evicted first");
                Assert.Equal(cap + 4, feed.Events[feed.Count - 1].provinceId, "the newest event must be retained");

                return $"buffer bounded at {cap}; oldest evicted, newest retained";
            },
            tier: "Execution", polarity: "negative",
            scenario: "More distinct events than the cap are recorded",
            expectation: "Count stops at the cap and the oldest entries are dropped, not the newest");

            // --- Border tension gated on hostility -------------------------------------------------
            yield return new SynapseTestCase("WorldNews_BorderTensionRequiresHostility", () =>
            {
                var contested = new ProvinceSnapshot { provinceId = 2, regionName = "Ashfall", contested = true };
                contested.contenderIds.AddRange(new[] { "X", "Y" });
                contested.contenderNames.AddRange(new[] { "Iron Compact", "Verdant League" });
                var snap = new List<ProvinceSnapshot> { contested };

                var friendly = WorldMapChangeDetector.DetectBorderTension(snap, 0, (a, b) => false);
                Assert.Equal(0, friendly.Count, "a contested frontier between friendly factions is not a tension story");

                var hostile = WorldMapChangeDetector.DetectBorderTension(snap, 0, (a, b) => true);
                Assert.Equal(1, hostile.Count, "a contested frontier between hostile factions should emit one tension event");
                Assert.Equal(WorldNewsEventKind.BorderTension, hostile[0].kind, "wrong event kind");

                // An uncontested region never produces tension regardless of hostility.
                var calm = WorldMapChangeDetector.DetectBorderTension(new List<ProvinceSnapshot> { Prov(3, "Calm", "Z", "Z-clan") }, 0, (a, b) => true);
                Assert.Equal(0, calm.Count, "an uncontested region should never produce tension");

                return "tension fires only when a contested frontier's contenders are hostile";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A contested region is evaluated for tension under friendly vs hostile relations",
            expectation: "No event when friendly; one when hostile; never on an uncontested region");

            // --- Ideology shift inert without a distribution, live with one ------------------------
            yield return new SynapseTestCase("WorldNews_IdeologyShiftInertUntilDistribution", () =>
            {
                // Production snapshots carry no belief (R&T#34 not built) → the detector must be inert.
                var before = new List<ProvinceSnapshot> { Prov(4, "Beliefless", "A", "A-clan") };
                var after = new List<ProvinceSnapshot> { Prov(4, "Beliefless", "A", "A-clan") };
                var inert = WorldMapChangeDetector.DetectIdeologyShifts(before, after, 0);
                Assert.Equal(0, inert.Count, "with no belief data the ideology-shift detector must emit nothing");

                // With a synthetic belief that changes, the wired path fires — proving it is gated, not broken.
                var b2 = new ProvinceSnapshot { provinceId = 4, regionName = "Beliefland", primaryBeliefId = "OldFaith", primaryBeliefName = "the Old Faith" };
                var a2 = new ProvinceSnapshot { provinceId = 4, regionName = "Beliefland", primaryBeliefId = "NewFaith", primaryBeliefName = "the New Creed" };
                var live = WorldMapChangeDetector.DetectIdeologyShifts(new List<ProvinceSnapshot> { b2 }, new List<ProvinceSnapshot> { a2 }, 0);
                Assert.Equal(1, live.Count, "a genuine belief change on a synthetic snapshot should emit one event");
                Assert.Equal(WorldNewsEventKind.IdeologyShift, live[0].kind, "wrong event kind");

                return "ideology-shift is inert with no belief data, fires when a belief genuinely changes";
            },
            tier: "Execution", polarity: "negative",
            scenario: "Ideology-shift detection runs with (production) null beliefs and with synthetic beliefs",
            expectation: "Nothing emitted without a distribution; the wired path fires when one exists");

            // --- Settlement founded ----------------------------------------------------------------
            yield return new SynapseTestCase("WorldNews_SettlementFoundedDetectsNewKeys", () =>
            {
                var prev = new HashSet<string> { "existing-1" };
                var curr = new List<SettlementSnapshot>
                {
                    new SettlementSnapshot { key = "existing-1", regionName = "Old Town", factionId = "F", factionName = "Old Folk" },
                    new SettlementSnapshot { key = "brand-new", provinceId = 7, regionName = "Glasswind Reach", factionId = "G", factionName = "the Pilgrims" },
                };
                var events = WorldMapChangeDetector.DetectSettlementsFounded(prev, curr, 0);
                Assert.Equal(1, events.Count, "only the settlement whose key is new should emit");
                Assert.Equal(WorldNewsEventKind.SettlementFounded, events[0].kind, "wrong event kind");
                Assert.Equal("the Pilgrims", events[0].factionAName, "founding event should name the founder");
                Assert.Contains(events[0].ToNewsLine(), "founded a new settlement", "settlement news line should read as a founding");

                return "settlement-founded fires for a new key and ignores an existing one";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A settlement set with one pre-existing and one new key is diffed",
            expectation: "Exactly one founding event, for the new key only");
        }
    }
}
