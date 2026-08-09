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
                Assert.True(map != null, "no map available to spawn a dwelling occupant on");

                // Deterministic seam (#29). The old case ran the full DwellingStructureGenerator.Generate,
                // whose random placement search and per-dwelling 66% spawn chance meant ~4% of runs
                // spawned nobody — and a constrained map could place no dwellings at all — so the
                // "spawned > 0" assert failed intermittently with no bug behind it. This spawns one
                // occupant through the same SetResident write path Generate uses, at a cell we pick,
                // so the marking and provider round-trip are what is under test, not the RNG.
                IntVec3 cell = IntVec3.Invalid;
                for (int i = 0; i < 500; i++)
                {
                    IntVec3 c = CellFinder.RandomCell(map);
                    if (c.Standable(map) && !c.Fogged(map)) { cell = c; break; }
                }
                if (cell == IntVec3.Invalid) cell = map.Center;

                Pawn pawn = null;
                try
                {
                    pawn = DwellingStructureGenerator.SpawnResidentForTesting(map, cell);

                    Assert.True(pawn != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike,
                        "the dwelling-resident write path did not spawn a humanlike pawn");
                    Assert.True(!pawn.Destroyed && pawn.Spawned,
                        "the generated occupant did not actually spawn on the map");

                    // The write path must mark its occupant a resident...
                    Assert.True(ResidencyUtility.IsResident(pawn),
                        "a generated dwelling occupant was not marked resident");

                    // ...and Core must reach the same answer through the provider it never holds a
                    // reference to. If registration silently failed, this is where it shows.
                    Assert.True(SynapseCoreProviders.IsResident(pawn),
                        "Core's provider disagrees: the generated occupant does not read as resident through Core");

                    return "generated dwelling occupant is resident, and Core agrees via the provider";
                }
                finally
                {
                    // Restore what we touched: the pawn (and its one-member defend lord, which
                    // disposes itself once empty). A stray outlander is exactly what a later case
                    // could trip over.
                    if (pawn != null && !pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
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
