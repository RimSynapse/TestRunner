using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Satisfies "All Empire patches report bound; none silently no-op" from
    /// Regions-and-Territories#16.
    ///
    /// <para>Both mods patch Empire by reflection, because the target types belong to an optional
    /// mod and an unresolvable target must be a logged skip rather than a load failure. That is the
    /// right design and it carries a specific hazard: <b>a patch that never bound looks exactly like
    /// one that did.</b> The economy, taxation and military models all read through those hooks, so
    /// every other 0.7 verification result is worthless if a hook is quietly absent.</para>
    ///
    /// <para>Both mods do log each branch. This does not read those logs — <c>Log.Messages</c> is a
    /// bounded buffer, and a check that can be lost to buffer pressure is not a check. It asks
    /// Harmony which owners are attached to the method, which is the fact itself rather than a
    /// report of it.</para>
    /// </summary>
    public static class EmpireIntegrationCases
    {
        private const string FactionsOwner = "rimsynapse.factions";

        /// <summary>
        /// An Empire method a RimSynapse mod is expected to patch when Empire is loaded.
        /// <paramref name="Consequence"/> names what silently stops working if it is not bound,
        /// because "patch missing" is not actionable on its own.
        /// </summary>
        private struct Expected
        {
            public string TypeName;
            public string MethodName;
            public string Owner;
            public string Consequence;
        }

        private static readonly Expected[] Targets =
        {
            new Expected {
                TypeName = "FactionColonies.ResourceFC", MethodName = "CalculateProductionBase",
                Owner = FactionsOwner,
                Consequence = "regional production scaling is not applied; Empire uses its own unmodified figures",
            },
            new Expected {
                TypeName = "FactionColonies.ResourceFC", MethodName = "CalculateProductionMult",
                Owner = FactionsOwner,
                Consequence = "the population curve is not applied",
            },
            new Expected {
                TypeName = "FactionColonies.WorldObjectComp_SettlementMilitary", MethodName = "SendMilitary",
                Owner = FactionsOwner,
                Consequence = "military reach and supply are not enforced; any deep strike is permitted",
            },
        };

        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Factions_EmpirePatchesAreBound", () =>
            {
                // Empire absent is a legitimate configuration, not a failure — but it must be said
                // out loud, because a silent pass here would read as "the integration works".
                if (GenTypes.GetTypeInAnyAssembly("FactionColonies.ResourceFC") == null)
                    return "SKIPPED: Empire (FactionColonies) is not loaded, so there is nothing to bind to";

                var unbound = new List<string>();
                var bound = new List<string>();

                foreach (var t in Targets)
                {
                    var type = GenTypes.GetTypeInAnyAssembly(t.TypeName);
                    if (type == null)
                    {
                        unbound.Add($"{t.TypeName} not found in any assembly — {t.Consequence}");
                        continue;
                    }

                    // No signature given: an overload set is resolved by name and every candidate
                    // checked. Pinning one signature would make this case fail on an Empire update
                    // that added an overload, which is noise rather than a finding.
                    var methods = AccessTools.GetDeclaredMethods(type)
                        .Where(m => m.Name == t.MethodName)
                        .ToList();

                    if (methods.Count == 0)
                    {
                        unbound.Add($"{t.TypeName}.{t.MethodName} does not exist — {t.Consequence}");
                        continue;
                    }

                    bool anyBound = methods.Any(m => OwnersOf(m).Contains(t.Owner, StringComparer.OrdinalIgnoreCase));
                    if (anyBound)
                        bound.Add($"{type.Name}.{t.MethodName}");
                    else
                        unbound.Add($"{t.TypeName}.{t.MethodName} exists but carries no {t.Owner} patch — {t.Consequence}");
                }

                Assert.True(unbound.Count == 0,
                    $"{unbound.Count} Empire hook(s) not bound: " + string.Join(" | ", unbound));

                return $"all {bound.Count} Empire hook(s) bound: {string.Join(", ", bound)}";
            });

            yield return new SynapseTestCase("Factions_HarmonyDebugIsOff", () =>
            {
                // Harmony.DEBUG writes every generated method to harmony.log.txt on the desktop and
                // measurably slows startup. It was shipped enabled in Factions' constructor
                // (Factions#51). It is global static state, so any mod setting it affects the whole
                // load — which is why this is asserted rather than trusted.
                Assert.True(!Harmony.DEBUG,
                    "Harmony.DEBUG is enabled: every patch is being dumped to harmony.log.txt and startup is slower for it");

                return "Harmony.DEBUG off";
            });
        }

        /// <summary>
        /// Harmony owner ids attached to <paramref name="method"/>, across prefix, postfix,
        /// transpiler and finalizer.
        /// </summary>
        private static HashSet<string> OwnersOf(MethodBase method)
        {
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var info = Harmony.GetPatchInfo(method);
            if (info == null) return owners;

            foreach (var group in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
            {
                if (group == null) continue;
                foreach (var p in group)
                    if (!string.IsNullOrEmpty(p.owner)) owners.Add(p.owner);
            }

            return owners;
        }
    }
}
