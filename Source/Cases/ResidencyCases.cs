using System.Collections.Generic;
using System.Linq;
using RimSynapse.RegionsAndTerritories;
using RimSynapse.RegionsAndTerritories.Residency;
using RimWorld;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers residency end to end in a running game: the comp reaching real pawn defs, the write
    /// path in dwelling generation, and the answer travelling back to Core through the provider.
    ///
    /// Background: residency moved out of Core's <c>SynapseCorePawnComp</c> and into Regions and
    /// Territories, which generates the dwellings and was always the only writer. The sandbox suites
    /// cover the rules, but three things are only true in a running game and were unproven until
    /// these cases existed:
    ///
    ///   * ResidencyInjector attaching the comp to real humanlike ThingDefs — it is
    ///     StaticConstructorOnStartup def surgery over DefDatabase, with nothing to assert offline.
    ///   * DwellingStructureGenerator actually marking the occupants it spawns. Dwelling generation
    ///     runs only during map generation, so a -quicktest run that stops at the world map never
    ///     touches the write path at all.
    ///   * The provider round trip. R&amp;T registers Func&lt;Pawn,bool&gt; into
    ///     SynapseCoreProviders.Residency by reflection, with no assembly reference either way. Both
    ///     halves compile and pass in isolation whether or not they ever meet.
    /// </summary>
    public static class ResidencyCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Regions_ResidencyCompIsInjected", () =>
            {
                int humanlike = DefDatabase<ThingDef>.AllDefs
                    .Count(d => d.race != null && d.race.Humanlike);
                int withComp = DefDatabase<ThingDef>.AllDefs
                    .Count(d => d.race != null && d.race.Humanlike
                                && d.comps != null
                                && d.comps.Any(c => c.compClass == typeof(ResidentPawnComp)));

                Assert.True(humanlike > 0, "no humanlike ThingDefs found at all");
                Assert.True(withComp == humanlike,
                    $"expected the residency comp on all {humanlike} humanlike defs, found {withComp}");

                return $"residency comp on all {withComp} humanlike def(s)";
            });

            yield return new SynapseTestCase("Regions_ResidencyProviderIsRegistered", () =>
            {
                // Registered by reflection from R&T, which holds no reference to Core. If the member
                // were renamed on either side both mods would still build and this would be null.
                Assert.True(SynapseCoreProviders.Residency != null,
                    "R&T did not register a residency provider with Core");

                Assert.True(SynapseCoreProviders.IsResident(null) == false,
                    "a null pawn must not read as a resident");

                return "residency provider registered and answering";
            });

            yield return new SynapseTestCase("Regions_DwellingOccupantsAreResidents", () =>
            {
                Map map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
                Assert.True(map != null, "no map available to generate dwellings on");

                var before = new HashSet<Pawn>(map.mapPawns.AllPawns);
                List<Pawn> spawned = null;

                try
                {
                    // The real write path. Generate spawns dwellings and their occupants exactly as
                    // it does during settlement map generation, and marks each occupant resident.
                    DwellingStructureGenerator.Generate(map, 3);

                    spawned = map.mapPawns.AllPawns
                        .Where(p => !before.Contains(p) && p.RaceProps != null && p.RaceProps.Humanlike)
                        .ToList();

                    Assert.True(spawned.Count > 0,
                        "dwelling generation spawned no humanlike pawns, so the write path was not exercised");

                    var residents = spawned.Where(p => ResidencyUtility.IsResident(p)).ToList();
                    Assert.True(residents.Count == spawned.Count,
                        $"expected all {spawned.Count} generated occupant(s) to be residents, got {residents.Count}");

                    // The whole point of the provider: Core must reach the same answer without
                    // knowing anything about R&T. If registration silently failed, this is where it
                    // shows.
                    var viaCore = spawned.Where(p => SynapseCoreProviders.IsResident(p)).ToList();
                    Assert.True(viaCore.Count == spawned.Count,
                        $"Core's provider saw {viaCore.Count} of {spawned.Count} residents; R&T and Core disagree");

                    return $"{spawned.Count} generated occupant(s) resident, and Core agrees via the provider";
                }
                finally
                {
                    // Restore what we touched, per the case conventions. The pawns are what later
                    // cases could trip over — a colony that suddenly contains outlanders. The
                    // structures Generate also places are left: they are inert scenery, removing
                    // them means reversing terrain and roof changes too, and the suite shuts the
                    // game down immediately after. Say so rather than implying a full rollback.
                    if (spawned != null)
                    {
                        foreach (var p in spawned)
                        {
                            if (p != null && !p.Destroyed) p.Destroy(DestroyMode.Vanish);
                        }
                    }
                }
            });

            yield return new SynapseTestCase("Regions_NonResidentsReadFalse", () =>
            {
                // Guards the opposite error: a provider that says yes to everyone would pass the
                // case above and be just as wrong.
                Map map = Find.CurrentMap ?? Find.Maps.FirstOrDefault();
                Assert.True(map != null, "no map available");

                var colonists = map.mapPawns.FreeColonists
                    .Where(p => !ResidencyUtility.IsResident(p))
                    .ToList();

                Assert.True(colonists.Count > 0,
                    "expected at least one non-resident colonist to check against");

                foreach (var p in colonists)
                {
                    Assert.True(SynapseCoreProviders.IsResident(p) == false,
                        $"{p.LabelShortCap} is not a resident but Core's provider says otherwise");
                }

                return $"{colonists.Count} non-resident colonist(s) read false through Core";
            });
        }
    }
}
