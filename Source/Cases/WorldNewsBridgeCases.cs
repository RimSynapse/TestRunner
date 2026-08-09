using System.Collections.Generic;
using Verse;
using RimSynapse.WorldNews.Integration;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers the WorldNews → Regions-and-Territories availability seam
    /// (<see cref="RegionsAndTerritoriesBridge"/>), the foundation of the 0.8 world-map work
    /// (WorldNews#12). R&amp;T is an <b>optional</b> dependency: WorldNews references its assembly
    /// for typed access but must detect its presence and gate every call, so it loads and runs with
    /// R&amp;T absent.
    ///
    /// <para>The test modlist includes Regions and Territories (it is under test in its own cases),
    /// so this run exercises the <b>present</b> branch: detection must report true, and the seam's
    /// guarded worker must bind R&amp;T's types and read world state without throwing. That the
    /// worker runs at all is itself the proof the direct assembly reference resolved at runtime — a
    /// mis-declared reference would have dropped WorldNews's whole assembly silently instead.</para>
    ///
    /// <para>Structural, not log-scraping: it asks the seam directly rather than reading startup
    /// output, so a rolled <c>Log.Messages</c> buffer cannot turn it green.</para>
    /// </summary>
    public static class WorldNewsBridgeCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("WorldNews_RegionsBridgeDetectsRT", () =>
            {
                Assert.True(RegionsAndTerritoriesBridge.Active,
                    "RegionsAndTerritoriesBridge.Active is false, but Regions and Territories is in the " +
                    "test modlist. Either detection is broken or WorldNews loaded before R&T and lost the " +
                    "reference (check Core_DeclaredLoadOrderRespected).");

                // The guarded worker touches R&T types. Reaching it proves the reference bound; a
                // true result with a non-null summary proves it read R&T's world state cleanly.
                bool described = RegionsAndTerritoriesBridge.TryDescribeWorldState(out string summary);
                Assert.True(described,
                    "Active was true but TryDescribeWorldState returned false — the guard and the worker disagree.");
                Assert.NotNull(summary, "TryDescribeWorldState reported success but produced no summary");

                return $"R&T detected; seam read world state: \"{summary}\"";
            },
            tier: "Execution",
            polarity: "positive",
            scenario: "WorldNews queries the R&T availability seam with R&T loaded",
            expectation: "Detection reports active and the guarded worker reads R&T world state without throwing");
        }
    }
}
