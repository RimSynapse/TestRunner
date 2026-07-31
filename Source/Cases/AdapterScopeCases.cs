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
    /// Catches one profile claiming another mod's world objects (Regions-and-Territories#33).
    ///
    /// <para>Adapter type rules are <b>not scoped to the mod that declares them</b>.
    /// <c>WorldObjectAdapterRegistry.TryClassify</c> offers every world object to every active
    /// adapter in ascending priority order and takes the first non-Unknown answer, and
    /// <c>TypeMatch.TypeNameContains</c> matches on a bare substring. A profile that sorts earlier
    /// therefore silently outranks the profile that actually understands the object.</para>
    ///
    /// <para>This was not hypothetical. The Vanilla Expanded Framework profile carried a
    /// <c>TypeNameContains "Settlement"</c> rule at priority 120; World Domination sits at 130 and
    /// names two of its travelling parties <c>WorldObject_Traveler_SettlementBuy</c> and
    /// <c>_SettlementGift</c>. Had VEF's markers resolved, those moving parties would have been
    /// classified as territory-holding settlements and World Domination's own profile — which calls
    /// them caravans — would never have been consulted. The only reason it never happened is that
    /// VEF's markers were stale for unrelated reasons (#31).</para>
    /// </summary>
    public static class AdapterScopeCases
    {
        /// <summary>
        /// Type namespaces that a different mod's profile is *expected* to govern.
        ///
        /// <para>Vanilla Outposts Expanded now ships inside Vanilla Expanded Framework, so
        /// <c>Outposts.*</c> types are contributed by VEF's assembly while belonging, correctly, to
        /// the VOE profile. That is a real fact about the ecosystem rather than a defect, so it is
        /// recorded here instead of being allowed to weaken the check for everything else.</para>
        /// </summary>
        private static readonly Dictionary<string, string> ExpectedForeignOwners =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Outposts.", "voe" },
            };

        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Regions_AdapterRulesDoNotReachAcrossMods", () =>
            {
                var adapters = WorldObjectAdapterRegistry.Adapters?
                    .OfType<ReflectionWorldObjectAdapter>()
                    .ToList();

                if (adapters == null || adapters.Count == 0)
                    return "SKIPPED: no reflection adapters registered";

                // packageId -> the adapter that ought to answer for that mod's types.
                var ownerByPackage = new Dictionary<string, ReflectionWorldObjectAdapter>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in adapters)
                {
                    string pkg = a.Profile?.packageId;
                    if (!string.IsNullOrEmpty(pkg) && !ownerByPackage.ContainsKey(pkg)) ownerByPackage[pkg] = a;
                }

                if (ownerByPackage.Count == 0)
                    return "SKIPPED: no profile declares a packageId, so ownership cannot be attributed";

                var collisions = new List<string>();
                int typesChecked = 0, modsChecked = 0;

                foreach (var pair in ownerByPackage)
                {
                    var types = WorldObjectTypesFrom(pair.Key);
                    if (types.Count == 0) continue;
                    modsChecked++;

                    foreach (var t in types)
                    {
                        typesChecked++;

                        // Registry order, first non-Unknown wins — the same walk TryClassify does,
                        // using the adapters' own matcher rather than a reimplementation of it.
                        ReflectionWorldObjectAdapter answering = null;
                        foreach (var a in adapters)
                        {
                            if (!SafeIsActive(a)) continue;
                            if (a.EvaluateTypeRules(t) != WorldObjectKind.Unknown) { answering = a; break; }
                        }

                        if (answering == null) continue;                 // unclassified is a separate concern
                        if (answering == pair.Value) continue;           // the owning profile answered
                        if (IsExpectedForeignOwner(t, answering)) continue;

                        collisions.Add($"{t.FullName} (from {pair.Key}) is classified by the " +
                                       $"'{answering.Profile?.adapterId}' profile as {answering.EvaluateTypeRules(t)}");
                    }
                }

                Assert.True(collisions.Count == 0,
                    $"{collisions.Count} world object type(s) claimed by the wrong mod's profile: " +
                    string.Join(" | ", collisions.Take(4)));

                return $"{typesChecked} world-object type(s) across {modsChecked} profiled mod(s); " +
                       "each classified by its own mod's profile";
            });
        }

        private static bool IsExpectedForeignOwner(Type t, ReflectionWorldObjectAdapter answering)
        {
            string full = t.FullName ?? string.Empty;
            foreach (var kv in ExpectedForeignOwners)
            {
                if (full.StartsWith(kv.Key, StringComparison.Ordinal) &&
                    string.Equals(answering.Profile?.adapterId, kv.Value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A throwing <c>IsActive</c> must not fail the case for the wrong reason — the registry
        /// guards it the same way at runtime.
        /// </summary>
        private static bool SafeIsActive(ReflectionWorldObjectAdapter a)
        {
            try { return a.IsActive; }
            catch { return false; }
        }

        private static List<Type> WorldObjectTypesFrom(string packageId)
        {
            var result = new List<Type>();
            try
            {
                foreach (var pack in LoadedModManager.RunningModsListForReading)
                {
                    if (pack?.PackageId == null ||
                        !pack.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)) continue;

                    var asms = pack.assemblies?.loadedAssemblies;
                    if (asms == null) continue;

                    foreach (var asm in asms)
                    {
                        if (asm == null) continue;
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch (ReflectionTypeLoadException ex) { types = ex.Types ?? new Type[0]; }

                        foreach (var t in types)
                        {
                            if (t == null || t.IsAbstract) continue;
                            if (typeof(WorldObject).IsAssignableFrom(t)) result.Add(t);
                        }
                    }
                }
            }
            catch
            {
                // An enumeration failure means "cannot check this mod", not "this mod is fine".
                // The count in the pass detail makes a silently empty sweep visible.
            }
            return result;
        }
    }
}
