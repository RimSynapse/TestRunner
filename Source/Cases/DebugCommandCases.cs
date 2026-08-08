using System.Collections.Generic;
using RimSynapse;
using RimSynapse.Comps;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Debug commands (Core#81 / Psychology#52). Deterministic coverage of the memory debug logic and a
    /// structural check that all five debug tools are registered. The live-LLM commands
    /// (debug_generate_memory / debug_run_evaluation) are exercised manually via the DevMode gizmos.
    /// </summary>
    public static class DebugCommandCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            // Positive: seed a memory and read it back.
            yield return new SynapseTestCase("Core_DebugAddAndDumpMemory", () =>
            {
                var comp = new SynapseCorePawnComp();
                string memId = SynapseCoreDebug.AddMemory(comp, "shared a joke with Tynan", "social", 0.3f,
                    new List<string> { "conversation" }, "Tynan", false);
                Assert.NotEmpty(memId, "AddMemory returns a memId");
                Assert.Equal(1, comp.memories.Count, "one memory added");
                string dump = SynapseCoreDebug.DumpMemories(comp);
                Assert.Contains(dump, "shared a joke", "dump shows the seeded memory");
                Assert.Contains(dump, "conversation", "dump shows tags");
                return $"memId={memId}";
            });

            // Negative: an empty summary is rejected and adds nothing.
            yield return new SynapseTestCase("Core_DebugAddMemoryRejectsEmptySummary", () =>
            {
                var comp = new SynapseCorePawnComp();
                string memId = SynapseCoreDebug.AddMemory(comp, "", "social", 0.3f, null, null, false);
                Assert.True(memId == null, "empty summary is rejected");
                Assert.Equal(0, comp.memories.Count, "no memory added for an empty summary");
                return "empty summary rejected";
            });

            // Positive: forcing maintenance prunes a lone chit-chat and leaves a long-term memory.
            yield return new SynapseTestCase("Core_DebugRunMaintenancePrunes", () =>
            {
                SynapseCorePawnComp.MemoryDecayMultiplier = 1.0f;
                var comp = new SynapseCorePawnComp();
                SynapseCoreDebug.AddMemory(comp, "idle small talk", "social", 0.1f, null, null, false);
                SynapseCoreDebug.AddMemory(comp, "a defining trauma", "EventReflection", 1.0f,
                    new List<string> { "Death" }, null, true);
                string result = SynapseCoreDebug.RunMaintenance(comp);
                Assert.Contains(result, "pruned 1", "the lone chit-chat is pruned");
                Assert.Equal(1, comp.memories.Count, "the long-term memory survives");
                return result;
            });

            // Structural: every debug tool (Core + Psychology) is registered.
            yield return new SynapseTestCase("Core_DebugToolsRegistered", () =>
            {
                string[] names =
                {
                    "debug_add_memory", "debug_dump_memories", "debug_run_memory_maintenance",
                    "debug_generate_memory", "debug_run_evaluation"
                };
                foreach (var n in names)
                    Assert.True(SynapseToolRegistry.IsToolRegistered(n), $"{n} must be registered");
                return $"{names.Length} debug tools registered";
            });
        }
    }
}
