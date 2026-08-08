using System.Collections.Generic;
using System.Linq;
using Verse;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimSynapse.Conversations;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Conversation depth work (Conversations#28): the context resolver's memory tiers, per-pair topic
    /// anti-repetition history, and the pre-seed pool's caps + variety. Deterministic — synthetic comps.
    /// </summary>
    public static class ConversationCases
    {
        private static WeightedMemory Mem(string summary, float weight, string type, long absTick,
            bool longTerm = false, List<string> tags = null)
        {
            return new WeightedMemory
            {
                summary = summary, weight = weight, baseWeight = weight, memoryType = type,
                absTick = absTick, isLongTerm = longTerm, tags = tags ?? new List<string>()
            };
        }

        public static IEnumerable<SynapseTestCase> All()
        {
            // Topic anti-repetition: the ring buffer dedupes and caps, most-recent last.
            yield return new SynapseTestCase("Conversations_TopicHistoryAvoidsRepeats", () =>
            {
                var pc = new PawnConversation("A", "B", 0);
                foreach (var t in new[] { "T_A", "T_B", "T_C", "T_D", "T_E" }) pc.PushRecentTopic(t);
                Assert.Equal(4, pc.recentTopics.Count, "history caps at 4");
                Assert.False(pc.recentTopics.Contains("T_A"), "oldest is evicted");
                Assert.Equal("T_E", pc.recentTopics.Last(), "most recent is last");
                pc.PushRecentTopic("T_C"); // re-use moves it to the end without duplicating
                Assert.Equal(4, pc.recentTopics.Count, "re-using a topic does not grow the history");
                Assert.Equal("T_C", pc.recentTopics.Last(), "re-used topic becomes most recent");
                return $"history=[{string.Join(",", pc.recentTopics)}]";
            });

            // Context resolver: memory keys resolve against the 0.7.1 tiers.
            yield return new SynapseTestCase("Conversations_ContextResolvesMemoryTiers", () =>
            {
                long now = Find.TickManager != null ? Find.TickManager.TicksAbs : 100000L;
                var core = new SynapseCorePawnComp();
                core.AddMemory(Mem("chatted by the fire today", 0.2f, "social", now - 100));
                core.AddMemory(Mem("a dull thing a season ago", 0.2f, "social", now - 500000));
                var lt = Mem("the defining moment of their life", 0.9f, "EventReflection", now - 500000, longTerm: true);
                core.AddMemory(lt); lt.isLongTerm = true; lt.salience = 2f;
                core.AddMemory(Mem("their friend died in the raid", 0.9f, "EventReflection", now - 300, tags: new List<string> { "Death" }));

                string today = ConversationContextResolver.Resolve("memoriesToday", null, null, core);
                Assert.Contains(today ?? "", "chatted by the fire", "today tier surfaces today's memory");
                Assert.DoesNotContain(today ?? "", "season ago", "today tier excludes old memories");

                string longTerm = ConversationContextResolver.Resolve("memoriesLongTerm", null, null, core);
                Assert.Contains(longTerm ?? "", "defining moment", "long-term tier surfaces the consolidated memory");

                string grief = ConversationContextResolver.Resolve("griefMemories", null, null, core);
                Assert.Contains(grief ?? "", "friend died", "grief tier surfaces the Death-tagged memory");
                return "today / long-term / grief tiers all resolved";
            });

            // Pre-seed pool: per-pair cap and topic variety.
            yield return new SynapseTestCase("Conversations_PreGenPoolCapsAndVaries", () =>
            {
                var wc = new SynapseConversationsWorldComponent(Find.World);
                for (int i = 0; i < 5; i++)
                {
                    wc.AddToPool(new PreGeneratedConversation
                    {
                        initiatorId = "P1", recipientId = "P2", topicDefName = "Topic_" + i,
                        initiatorStatement = "hi " + i, recipientResponse = "hello " + i
                    });
                }
                Assert.Equal(SynapseConversationsWorldComponent.MaxPreGenPerPair, wc.PoolCountForPair("P1", "P2"),
                    "a single pair cannot exceed the per-pair cap");
                Assert.False(wc.PairNeedsFill("P1", "P2"), "a full pair reports no need to fill");
                Assert.Equal(SynapseConversationsWorldComponent.MaxPreGenPerPair, wc.PoolTopicsForPair("P1", "P2").Count,
                    "pooled topics are distinct (selection diversifies them)");
                return $"pair pool={wc.PoolCountForPair("P1", "P2")}, distinct topics={wc.PoolTopicsForPair("P1", "P2").Count}";
            });
        }
    }
}
