using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimSynapse.RegionsAndTerritories.Integration;
using RimWorld.Planet;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Pins the declared adapter profiles against the assemblies they claim to describe
    /// (Regions-and-Territories#16: "assumedMaxLevel is set from observed data for VOE and World
    /// Domination, or the level input is documented as permanently unused for them").
    ///
    /// <para>The profiles in <c>KnownModProfiles</c> name member names as strings — that is what
    /// makes the integration layer mod-agnostic, and it is also what lets a profile be quietly wrong.
    /// A member name that resolves against nothing costs no error: the reflection adapter returns its
    /// default and the caller reads a plausible zero.</para>
    ///
    /// <para>Reading the DLL from outside cannot answer this. A member name appearing in an assembly
    /// may be a <i>reference</i> to somebody else's member rather than a declaration — probing VOE's
    /// Outposts.dll for "MaxLevel" and "minLevel" finds both, and neither belongs to
    /// <c>Outposts.Outpost</c>. Only reflection against the live type distinguishes them, which is why
    /// this is an in-game case rather than a build-time check.</para>
    /// </summary>
    public static class AdapterProfileCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Regions_AdapterProfilesMatchRealAssemblies", () =>
            {
                var profiles = new[]
                {
                    KnownModProfiles.Empire(),
                    KnownModProfiles.VanillaOutpostsExpanded(),
                    KnownModProfiles.VanillaFactionsExpanded(),
                    KnownModProfiles.WorldDomination(),
                };

                var problems = new List<string>();
                var observed = new List<string>();
                int present = 0;

                foreach (var p in profiles)
                {
                    // Lambda, not a method group: GetTypeInAnyAssembly is overloaded, so Select
                    // cannot infer which one is meant.
                    var markers = (p.markerTypes ?? new string[0])
                        .Select(n => GenTypes.GetTypeInAnyAssembly(n))
                        .Where(t => t != null)
                        .ToList();

                    if (markers.Count == 0)
                    {
                        // Two very different states look identical here, and telling them apart is
                        // the whole reason profiles now carry a packageId.
                        //
                        // Mod absent: correct and expected — profiles ship for mods the player may
                        // not have. Mod present but no marker resolved: the marker names are wrong
                        // and the adapter is inert, which is exactly how Vanilla Expanded Framework
                        // went unnoticed after it renamed its assembly from VFECore to VEF (#31).
                        if (IsModActive(p.packageId))
                        {
                            problems.Add($"{p.displayName}: mod {p.packageId} is ACTIVE but none of its markerTypes " +
                                         $"[{string.Join(", ", p.markerTypes ?? new string[0])}] resolve — the adapter is " +
                                         $"inert and every object of this mod falls through. Candidate world-object types " +
                                         $"actually present: {SuggestWorldObjectTypes(p.packageId)}");
                        }
                        else
                        {
                            observed.Add($"{p.displayName}: not installed");
                        }
                        continue;
                    }
                    present++;

                    var pop = ResolvedOn(markers, p.populationMembers);
                    var lvl = ResolvedOn(markers, p.levelMembers);
                    var max = ResolvedOn(markers, p.maxLevelMembers);

                    // Population is load-bearing: PopulationDensityUtility and the sizing tiers both
                    // read it, and a profile that resolves none of its population members reports
                    // every settlement as empty without complaining.
                    if ((p.populationMembers?.Length ?? 0) > 0 && pop.Count == 0)
                    {
                        problems.Add($"{p.displayName}: none of its populationMembers " +
                                     $"[{string.Join(", ", p.populationMembers)}] exist on {markers[0].FullName} — " +
                                     "population reads as zero for every object of this kind");
                    }

                    // The level input is optional, but the profile must not claim a level ladder it
                    // cannot read. assumedMaxLevel > 0 with no readable level member is a
                    // contradiction: something will scale against a level that is always default.
                    if (p.assumedMaxLevel > 0 && lvl.Count == 0 && max.Count == 0)
                    {
                        problems.Add($"{p.displayName}: assumedMaxLevel={p.assumedMaxLevel} but none of " +
                                     $"[{string.Join(", ", p.levelMembers ?? new string[0])}] exist on " +
                                     $"{markers[0].FullName} — the level ladder is unreadable");
                    }

                    observed.Add($"{p.displayName}: pop[{Join(pop)}] level[{Join(lvl)}] " +
                                 $"assumedMaxLevel={p.assumedMaxLevel}");
                }

                Assert.True(problems.Count == 0, string.Join(" | ", problems));

                if (present == 0)
                    return "SKIPPED: none of the profiled mods are installed, so nothing could be pinned";

                // The detail is the deliverable here, not just a pass marker: it is the observed
                // record #16 asks for, and it lands in Player.log on every run.
                return $"{present} profiled mod(s) installed — {string.Join("; ", observed)}";
            });
        }

        private static bool IsModActive(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;

            // ModsConfig writes packageIds lowercased; profiles carry the author's casing.
            var active = ModsConfig.ActiveModsInLoadOrder;
            if (active == null) return false;

            foreach (var m in active)
            {
                if (m?.PackageId != null &&
                    m.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// World-object types the named mod actually contributes, so a marker-resolution failure
        /// carries its own fix rather than sending the reader back to a decompiler.
        ///
        /// <para>Only <see cref="WorldObject"/> subclasses are listed: those are the only types a
        /// marker is useful for, and a large framework assembly holds thousands of others.</para>
        /// </summary>
        private static string SuggestWorldObjectTypes(string packageId)
        {
            try
            {
                foreach (var pack in LoadedModManager.RunningModsListForReading)
                {
                    if (pack?.PackageId == null ||
                        !pack.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)) continue;

                    var asms = pack.assemblies?.loadedAssemblies;
                    if (asms == null) return "(mod contributes no loaded assembly)";

                    var names = new List<string>();
                    foreach (var asm in asms)
                    {
                        if (asm == null) continue;
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch (ReflectionTypeLoadException ex) { types = ex.Types ?? new Type[0]; }

                        foreach (var t in types)
                        {
                            if (t == null || t.IsAbstract) continue;
                            if (typeof(WorldObject).IsAssignableFrom(t)) names.Add(t.FullName);
                        }
                    }

                    if (names.Count == 0) return "(none — this mod defines no WorldObject subclass)";
                    names.Sort();
                    return names.Count <= 8
                        ? string.Join(", ", names)
                        : string.Join(", ", names.Take(8)) + $", (+{names.Count - 8} more)";
                }

                return "(mod not found among running mods)";
            }
            catch (Exception ex)
            {
                return $"(could not enumerate: [{ex.GetType().Name}] {ex.Message})";
            }
        }

        /// <summary>Which of <paramref name="names"/> exist as a field or property on any marker type.</summary>
        private static List<string> ResolvedOn(List<Type> markers, string[] names)
        {
            var found = new List<string>();
            if (names == null) return found;

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                foreach (var t in markers)
                {
                    if (t.GetProperty(n, Flags) != null || t.GetField(n, Flags) != null)
                    {
                        found.Add(n);
                        break;
                    }
                }
            }

            return found;
        }

        private static string Join(List<string> items)
        {
            return items.Count == 0 ? "none" : string.Join(",", items);
        }
    }
}
