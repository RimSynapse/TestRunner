using System.Collections.Generic;
using System.Linq;
using RimSynapse.RegionsAndTerritories;
using RimSynapse.RegionsAndTerritories.Integration;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers the compatibility flag that lets Regions and Territories be installed into a world
    /// already in progress.
    ///
    /// <para>The mod's placement rules assume they governed every settlement ever placed, so a
    /// world built without them is full of towns those rules would have refused. A world now
    /// records whether it enforces them, and one that predates the mod records that it does
    /// not.</para>
    ///
    /// <para>The load-time decision keys on <b>provinces, not the flag</b>, and that is the part
    /// worth pinning: a save made with 0.7 also has no flag yet, and reading the flag alone would
    /// silently strip governance from every existing player. These cases fail if anyone
    /// "simplifies" the check back to the flag.</para>
    /// </summary>
    public static class TerritorialOwnershipCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Regions_CompatAdoptedWhenSaveHasNoProvinces", () =>
            {
                var mgr = Find.World?.GetComponent<SynapseRegionManager>();
                Assert.True(mgr != null, "no SynapseRegionManager on the world");

                bool original = mgr.StrictTerritorialOwnership;
                var saved = new List<GeographicProvince>(mgr.ProvincesRaw);
                try
                {
                    // A world the mod was just added to: no province data of ours in the save.
                    mgr.ProvincesRaw.Clear();
                    mgr.ResetStrictOwnershipForTesting();
                    mgr.ResolveStrictOwnershipForTesting();

                    Assert.True(!mgr.StrictTerritorialOwnership,
                        "a save with no provinces must be adopted in compatibility mode, not strict");
                    return "no provinces in save -> compatibility mode";
                }
                finally
                {
                    mgr.ProvincesRaw.Clear();
                    mgr.ProvincesRaw.AddRange(saved);
                    mgr.StrictTerritorialOwnership = original;
                }
            });

            yield return new SynapseTestCase("Regions_StrictPreservedWhenSaveHasProvinces", () =>
            {
                var mgr = Find.World?.GetComponent<SynapseRegionManager>();
                Assert.True(mgr != null, "no SynapseRegionManager on the world");
                Assert.True(mgr.ProvincesRaw.Count > 0,
                    "this world has no provinces, so the case cannot distinguish the two paths");

                bool original = mgr.StrictTerritorialOwnership;
                try
                {
                    // A 0.7 save: provinces present, flag absent. Must stay strict — this is the
                    // regression that would silently disable governance for existing players.
                    mgr.ResetStrictOwnershipForTesting();
                    mgr.ResolveStrictOwnershipForTesting();

                    Assert.True(mgr.StrictTerritorialOwnership,
                        "a save that already has provinces was built under the rules and must keep them");
                    return $"{mgr.ProvincesRaw.Count} provinces in save -> strict preserved";
                }
                finally
                {
                    mgr.StrictTerritorialOwnership = original;
                }
            });

            yield return new SynapseTestCase("Regions_CompatModeStandsDownPlacementRules", () =>
            {
                var mgr = Find.World?.GetComponent<SynapseRegionManager>();
                Assert.True(mgr != null, "no SynapseRegionManager on the world");

                if (!WorldObjectIntegrationSettings.PlacementGovernanceActive)
                {
                    return "SKIPPED: placement governance is switched off, so there is nothing to stand down";
                }

                bool original = mgr.StrictTerritorialOwnership;
                try
                {
                    var faction = Find.FactionManager.AllFactions
                        .FirstOrDefault(f => !f.def.isPlayer && !f.Hidden);
                    Assert.True(faction != null, "no non-player faction to evaluate against");

                    // Find a tile the strict rules actually refuse. Without one the case proves
                    // nothing, so say so rather than passing on a vacuous comparison.
                    mgr.StrictTerritorialOwnership = true;
                    int refusedTile = -1;
                    int tiles = Find.WorldGrid.TilesCount;
                    for (int i = 0; i < tiles && refusedTile < 0; i += 97)
                    {
                        var d = WorldObjectPlacementUtility.Evaluate(i, faction, WorldObjectKind.Settlement);
                        if (!d.Allowed) refusedTile = i;
                    }

                    if (refusedTile < 0)
                    {
                        return "SKIPPED: strict rules refused no sampled tile, so there is no refusal to compare against";
                    }

                    mgr.StrictTerritorialOwnership = false;
                    var compat = WorldObjectPlacementUtility.Evaluate(refusedTile, faction, WorldObjectKind.Settlement);

                    Assert.True(compat.Allowed,
                        $"tile {refusedTile} was refused under strict and must be allowed in compatibility mode");

                    return $"tile {refusedTile} refused under strict, allowed in compatibility";
                }
                finally
                {
                    mgr.StrictTerritorialOwnership = original;
                }
            });
        }
    }
}
