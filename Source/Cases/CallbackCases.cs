using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Guards the deferred-callback pattern used throughout RimSynapse.
    ///
    /// LLM responses arrive off the main thread and are handed back via
    /// SynapseGameComponent.Enqueue, so the body runs on a later frame. A try/catch placed
    /// around the PromptAsync callback does NOT cover that body — anything thrown inside
    /// escapes to ProcessMainThreadQueue and is reported as a bare "Callback error" with no
    /// indication of which feature failed.
    ///
    /// The concrete case that motivated this: a ceremony response missing its narrative field
    /// (the quicktest mock answers a bare {"success": true}) left finalRecord null, and
    /// reading finalRecord.Length threw.
    /// </summary>
    public static class CallbackCases
    {
        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("Core_AllModsInstantiated", () =>
            {
                // A binary-incompatible change in Core (e.g. altering a public method
                // signature that companion DLLs bind to) surfaces as mod-instantiation or
                // static-constructor failures at startup. The rest of the suite can stay
                // green while entire mods are dead, so this failure mode needs its own name.
                var failures = RecentLogLines()
                    .Where(l => l.Contains("Error while instantiating a mod")
                             || l.Contains("Error in static constructor"))
                    .ToList();

                Assert.True(failures.Count == 0,
                    $"{failures.Count} mod(s) failed to initialise: " +
                    string.Join(" | ", failures.Take(3).Select(Shorten)));

                return "every mod instantiated cleanly";
            });

            yield return new SynapseTestCase("Core_NoUnhandledQueueCallbackErrors", () =>
            {
                var offenders = RecentLogLines()
                    .Where(l => l.IndexOf("Callback error", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                Assert.True(offenders.Count == 0,
                    $"{offenders.Count} unhandled main-thread queue failure(s): " +
                    string.Join(" | ", offenders.Take(3).Select(Shorten)));

                return "main-thread queue drained cleanly";
            });

            yield return new SynapseTestCase("Psychology_CeremonyRecordSurvivesEmptyResponse", () =>
            {
                // Under -quicktest the LLM is mocked and the ceremony prompt matches none of the
                // mock's keyword branches, so it answers {"success": true} — every field null.
                // The autotest fires a ceremony on load, so if that path still dereferences a
                // missing narrative it shows up here as a logged failure.
                var failures = RecentLogLines()
                    .Where(l => l.IndexOf("Failed to apply ceremony record", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                Assert.True(failures.Count == 0,
                    "ceremony record application threw: " + string.Join(" | ", failures.Take(2).Select(Shorten)));

                return "ceremony path handled the response without throwing";
            });
        }

        /// <summary>
        /// Verse.Log keeps the in-memory message buffer the dev console renders; reading it
        /// avoids depending on Player.log being flushed to disk while the game is still running.
        /// </summary>
        private static IEnumerable<string> RecentLogLines()
        {
            var messages = Log.Messages;
            if (messages == null) return Enumerable.Empty<string>();

            // Skip our own output. These cases search for phrases that a result line could
            // itself contain, which would let the suite match its own reporting.
            return messages
                .Select(m => m.text ?? string.Empty)
                .Where(t => t.IndexOf(SynapseTestReporter.Tag, StringComparison.Ordinal) < 0)
                .ToList();
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "<empty>";
            var oneLine = s.Replace("\r", " ").Replace("\n", " ");
            return oneLine.Length <= 160 ? oneLine : oneLine.Substring(0, 160) + "...";
        }
    }
}
