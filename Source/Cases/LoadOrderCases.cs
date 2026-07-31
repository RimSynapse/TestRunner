using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimSynapse;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Closes the hole in <c>Core_AllModsInstantiated</c> (TestRunner#6).
    ///
    /// <para>That sentinel scans the log for "Error while instantiating a mod" and "Error in static
    /// constructor". A mod whose entire assembly fails to resolve produces neither: RimWorld logs
    /// <c>ReflectionTypeLoadException getting types in assembly X</c>, then omits the assembly from
    /// <c>GenTypes.AllTypes</c>, so the mod's <c>Mod</c> subclass is never discovered and therefore
    /// never fails to instantiate. Factions ordered before Regions and Territories was completely
    /// dead — patches unbound, worldgen step gone, five tools missing — and the suite reported
    /// "every mod instantiated cleanly" (Factions#42).</para>
    ///
    /// <para><b>Both cases are structural, neither scrapes the log.</b> <c>Log.Messages</c> is a
    /// bounded buffer, so a startup noisy enough to roll it would give a log-scanning case a false
    /// PASS — precisely the failure mode that makes a suite untrustworthy. Asking ModsConfig for the
    /// order and asking each assembly for its types cannot be lost to buffer pressure.</para>
    /// </summary>
    public static class LoadOrderCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Core_DeclaredLoadOrderRespected", () =>
            {
                // The cause. Uses Core's own implementation, so the check the player gets at startup
                // and the check the suite makes cannot drift apart.
                var violations = SynapseLoadOrderCheck.FindViolations();

                Assert.True(violations.Count == 0,
                    $"{violations.Count} mod(s) load before a declared dependency: " +
                    string.Join(" | ", violations.Take(3).Select(v =>
                        $"{v.ModName} (pos {v.ModPosition + 1}) must load after {v.RequiredName} (pos {v.RequiredPosition + 1})")));

                int active = ModsConfig.ActiveModsInLoadOrder?.Count() ?? 0;
                return $"{active} active mod(s), every declared loadAfter satisfied";
            });

            yield return new SynapseTestCase("Core_EveryShippedAssemblyIsLive", () =>
            {
                // The symptom, and not specific to load order: any unresolvable reference produces
                // it, including a binary-incompatible change to Core's public surface.
                //
                // The first version of this case walked pack.assemblies.loadedAssemblies and called
                // GetTypes() on each — and PASSED while Factions was provably dead. RimWorld does
                // not keep an assembly it could not load types from, so that list contains only
                // healthy assemblies. Asking "did any of what loaded break" is the exact mistake
                // Core_AllModsInstantiated makes.
                //
                // So ask the other question: does every DLL a mod actually ships have a live
                // assembly? Zero-from-nonzero is unambiguous. GetTypes() is still called on what is
                // present, because an assembly can in principle be retained and still be broken —
                // that is a second way to fail, never a way to pass.
                var dead = new List<string>();
                int packsWithDlls = 0, liveAssemblies = 0;

                var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pack in LoadedModManager.RunningModsListForReading)
                {
                    var asms = pack?.assemblies?.loadedAssemblies;
                    if (asms == null) continue;
                    foreach (var a in asms)
                        if (a != null) loadedNames.Add(a.GetName().Name);
                }

                foreach (var pack in LoadedModManager.RunningModsListForReading)
                {
                    if (pack == null) continue;

                    var shipped = ShippedAssemblyNames(pack);
                    if (shipped.Count == 0) continue;
                    packsWithDlls++;

                    var live = pack.assemblies?.loadedAssemblies ?? new List<Assembly>();
                    liveAssemblies += live.Count;

                    foreach (string name in shipped)
                    {
                        // Loaded by this mod, or already loaded by another under the same simple
                        // name — RimWorld skips duplicates, and that is not a failure.
                        if (loadedNames.Contains(name)) continue;
                        dead.Add($"{pack.Name}: ships {name}.dll but no such assembly is loaded — that mod is inert");
                    }

                    foreach (var asm in live)
                    {
                        if (asm == null) continue;
                        try { asm.GetTypes(); }
                        catch (ReflectionTypeLoadException ex)
                        {
                            string why = ex.LoaderExceptions?.FirstOrDefault()?.Message ?? "no loader exception reported";
                            dead.Add($"{pack.Name} / {asm.GetName().Name}: {Shorten(why)}");
                        }
                        catch (Exception ex)
                        {
                            dead.Add($"{pack.Name} / {asm.GetName().Name}: [{ex.GetType().Name}] {Shorten(ex.Message)}");
                        }
                    }
                }

                Assert.True(dead.Count == 0,
                    $"{dead.Count} shipped assembly/assemblies are not live: " + string.Join(" | ", dead.Take(3)));

                return $"{packsWithDlls} mod(s) ship assemblies; all accounted for, {liveAssemblies} live";
            });
        }

        /// <summary>
        /// Simple names of the managed DLLs this mod actually ships for the running game version.
        ///
        /// <para>Uses <c>ModContentPack.foldersToLoadDescendingOrder</c> when available — that is
        /// RimWorld's own answer to "which folders are in play", so a mod shipping only a
        /// <c>1.5/Assemblies</c> DLL is not counted as failing under 1.6. Falls back to the root
        /// <c>Assemblies</c> folder, which is where a single-version mod puts them.</para>
        /// </summary>
        private static List<string> ShippedAssemblyNames(ModContentPack pack)
        {
            var names = new List<string>();
            var roots = new List<string>();

            try
            {
                var prop = typeof(ModContentPack).GetProperty("foldersToLoadDescendingOrder",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var folders = prop?.GetValue(pack, null) as IEnumerable<string>;
                if (folders != null) roots.AddRange(folders);
            }
            catch { /* fall through to RootDir */ }

            if (roots.Count == 0 && !string.IsNullOrEmpty(pack.RootDir)) roots.Add(pack.RootDir);

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string dir = System.IO.Path.Combine(root, "Assemblies");
                if (!System.IO.Directory.Exists(dir)) continue;

                foreach (string dll in System.IO.Directory.GetFiles(dir, "*.dll", System.IO.SearchOption.TopDirectoryOnly))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(dll);
                    if (!names.Contains(name)) names.Add(name);
                }
            }

            return names;
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 140 ? s : s.Substring(0, 140) + "...";
        }
    }
}
