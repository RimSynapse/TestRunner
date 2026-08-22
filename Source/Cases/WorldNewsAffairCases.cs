using System.Collections.Generic;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;
using RimSynapse.WorldNews.Relations;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers "settlement affairs": the random adjudication of a non-player settlement's daily event and
    /// the bounded, temporary short-term relation nudge a conflict outcome applies. Both the adjudication
    /// and the relation ledger are pure (the ledger takes the goodwill mechanism as a delegate), so the
    /// design invariants — <b>bounded to ±10</b>, and <b>decays back to zero</b> so the long-term
    /// baseline is untouched — are verified here with no live factions.
    /// </summary>
    public static class WorldNewsAffairCases
    {
        private static AffairContext Ctx(bool withRival) => new AffairContext
        {
            settlementFactionId = "settlerFaction", settlementFactionName = "the Marsh Wardens",
            rivalFactionId = withRival ? "rivalFaction" : null,
            rivalFactionName = withRival ? "the Dune Reavers" : null,
            regionName = "Thornmarch", provinceId = 5, nowTicks = 1000,
        };

        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("WorldNews_AffairConflictBoundedAndSigned", () =>
            {
                // conflictRoll 0 (<0.55) + rival present → conflict; outcomeRoll 0 → first conflict outcome
                // (a soured raid); magnitude clamped into [1,10].
                var soured = SettlementAffairGenerator.Adjudicate(Ctx(withRival: true), 0f, 0f, 10);
                Assert.Equal(WorldNewsEventKind.SettlementAffair, soured.evt.kind, "wrong event kind");
                Assert.Equal(-10, soured.relationDelta, "a soured conflict at max magnitude should be exactly -10");
                Assert.Equal("rivalFaction", soured.bId, "a conflict outcome must carry the rival as actor B");
                Assert.Contains(soured.evt.ToNewsLine(), "soured", "a soured outcome should read as souring relations");

                // An over-cap magnitude is still clamped to the ±10 ceiling.
                var overcap = SettlementAffairGenerator.Adjudicate(Ctx(true), 0f, 0f, 9999);
                Assert.True(overcap.relationDelta >= -SettlementAffairGenerator.MaxRelationDelta,
                    "delta must never exceed the ±MaxRelationDelta ceiling");

                // A warming outcome (last conflict entry) yields a positive delta.
                var warmed = SettlementAffairGenerator.Adjudicate(Ctx(true), 0f, 0.99f, 4);
                Assert.True(warmed.relationDelta > 0, "a warming conflict outcome should be positive");

                return "conflict outcomes are signed by tone and bounded to ±10";
            },
            tier: "Execution", polarity: "positive",
            scenario: "A settlement affair adjudicates to a conflict outcome",
            expectation: "A SettlementAffair event with a signed relation delta clamped to ±10");

            yield return new SynapseTestCase("WorldNews_AffairLocalHasNoRelationEffect", () =>
            {
                // No rival → only local (flavour) outcomes; never a relation delta.
                var local = SettlementAffairGenerator.Adjudicate(Ctx(withRival: false), 0f, 0f, 10);
                Assert.Equal(0, local.relationDelta, "a rival-less (local) affair must not move any relation");
                Assert.True(local.bId == null, "a local affair has no second faction");
                Assert.Equal(WorldNewsEventKind.SettlementAffair, local.evt.kind, "wrong event kind");

                return "local affairs are pure flavour — no relation change";
            },
            tier: "Execution", polarity: "negative",
            scenario: "A settlement with no rival available adjudicates its daily affair",
            expectation: "A flavour-only event and a zero relation delta");

            yield return new SynapseTestCase("WorldNews_RelationLedgerBoundedAndDecaysToZero", () =>
            {
                // A fake goodwill store the injected applier reads/writes, so the ledger's policy is
                // testable without live factions. Saturates at ±100 like the real thing.
                var store = new Dictionary<string, int>();
                string Key(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
                ShortTermRelationLedger.GoodwillApplier applier = (a, b, req) =>
                {
                    store.TryGetValue(Key(a, b), out int cur);
                    int next = System.Math.Max(-100, System.Math.Min(100, cur + req));
                    store[Key(a, b)] = next;
                    return next - cur; // actual applied
                };

                var ledger = new ShortTermRelationLedger();

                // Two souring affairs on one day must not push the pair past the ±10 cap.
                ledger.ApplyNudge("A", "B", -8, applier);
                ledger.ApplyNudge("A", "B", -8, applier);
                Assert.Equal(-10, store[Key("A", "B")], "the pair's short-term offset must be capped at ±10");
                Assert.Equal(1, ledger.Count, "the two nudges collapse into one bounded pair entry");

                // Decaying day by day must return goodwill exactly to the baseline (0) and clear the entry.
                int guard = 0;
                while (ledger.Count > 0 && guard++ < 50) ledger.TickDecay(applier);
                Assert.Equal(0, store[Key("A", "B")], "decay must return the pair to its original baseline");
                Assert.Equal(0, ledger.Count, "a fully-decayed pair is dropped from the ledger");

                return "pair offset capped at ±10 and decays cleanly back to the baseline";
            },
            tier: "Execution", polarity: "positive",
            scenario: "Repeated same-direction nudges on a pair, then daily decay",
            expectation: "Offset never exceeds ±10 and unwinds exactly to the original baseline");
        }
    }
}
