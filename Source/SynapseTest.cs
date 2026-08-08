using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// A single test case. <see cref="Run"/> throws <see cref="SynapseTestFailure"/> to fail;
    /// returning normally is a pass. The optional returned string is logged as pass detail.
    ///
    /// <para>Optional tier metadata drives the human-readable wiki report (Learning/Test_Report.md).
    /// A case with a <see cref="Tier"/> set is included in the report; untiered cases are plain
    /// unit tests that only report to the log.</para>
    /// </summary>
    public class SynapseTestCase
    {
        public string Name;
        public Func<string> Run;

        /// <summary>Skip predicate — return a reason to skip, or null to run.</summary>
        public Func<string> SkipReason;

        /// <summary>"Determination" (upstream / LLM decision) | "Execution" (downstream / mechanics) | null.</summary>
        public string Tier;
        /// <summary>"positive" | "negative" | null — whether the scenario should or should NOT happen.</summary>
        public string Polarity;
        /// <summary>Human-readable scenario, for the report.</summary>
        public string Scenario;
        /// <summary>Human-readable expected outcome, for the report.</summary>
        public string Expectation;

        public SynapseTestCase(string name, Func<string> run, Func<string> skipReason = null,
            string tier = null, string polarity = null, string scenario = null, string expectation = null)
        {
            Name = name;
            Run = run;
            SkipReason = skipReason;
            Tier = tier;
            Polarity = polarity;
            Scenario = scenario;
            Expectation = expectation;
        }
    }

    /// <summary>One tiered case's outcome, accumulated for the wiki report.</summary>
    public class SynapseTestRecord
    {
        public string Name, Tier, Polarity, Scenario, Expectation, Outcome, Detail;
    }

    /// <summary>Thrown by assertions to mark a case failed. Not an error condition for the game.</summary>
    public class SynapseTestFailure : Exception
    {
        public SynapseTestFailure(string message) : base(message) { }
    }

    /// <summary>Assertions for test cases. All failures throw <see cref="SynapseTestFailure"/>.</summary>
    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new SynapseTestFailure(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new SynapseTestFailure(message);
        }

        public static void NotNull(object value, string message)
        {
            if (value == null) throw new SynapseTestFailure(message + " (was null)");
        }

        public static void NotEmpty(string value, string message)
        {
            if (string.IsNullOrEmpty(value)) throw new SynapseTestFailure(message + " (was null or empty)");
        }

        public static void Equal(object expected, object actual, string message)
        {
            if (!Equals(expected, actual))
                throw new SynapseTestFailure($"{message} (expected '{expected}', got '{actual}')");
        }

        public static void Contains(string haystack, string needle, string message)
        {
            if (haystack == null || !haystack.Contains(needle))
                throw new SynapseTestFailure($"{message} (expected to contain '{needle}', got '{Truncate(haystack)}')");
        }

        public static void DoesNotContain(string haystack, string needle, string message)
        {
            if (haystack != null && haystack.Contains(needle))
                throw new SynapseTestFailure($"{message} (expected NOT to contain '{needle}', got '{Truncate(haystack)}')");
        }

        private static string Truncate(string s, int max = 200)
        {
            if (s == null) return "<null>";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }

    /// <summary>
    /// Emits results in the exact format the PowerShell harness parses:
    ///   [SYNAPSE-TEST] PASS &lt;case&gt; | &lt;detail&gt;
    ///   [SYNAPSE-TEST] SUMMARY passed=N failed=M skipped=K
    ///
    /// Uses Log.Message for every line — including failures — on purpose. readlog.ps1 buckets
    /// lines matching /error/ as blocking build errors, so routing FAILs through Log.Error would
    /// double-count them. Failures are already surfaced by the FAIL token itself.
    /// </summary>
    public static class SynapseTestReporter
    {
        public const string Tag = "[SYNAPSE-TEST]";

        public static void Pass(string name, string detail)
        {
            Log.Message($"{Tag} PASS {name} | {Sanitize(detail)}");
        }

        public static void Fail(string name, string reason)
        {
            Log.Message($"{Tag} FAIL {name} | {Sanitize(reason)}");
        }

        public static void Skip(string name, string reason)
        {
            Log.Message($"{Tag} SKIP {name} | {Sanitize(reason)}");
        }

        public static void Summary(int passed, int failed, int skipped)
        {
            Log.Message($"{Tag} SUMMARY passed={passed} failed={failed} skipped={skipped}");
        }

        public static void Info(string message)
        {
            Log.Message($"{Tag} INFO {Sanitize(message)}");
        }

        /// <summary>Results are one-line records; collapse newlines so the parser sees one entry.</summary>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }

    /// <summary>Executes cases and reports results. Never lets a case take the game down.</summary>
    public static class SynapseTestRunner
    {
        public static int Passed;
        public static int Failed;
        public static int Skipped;

        /// <summary>Outcomes of tiered cases, for the wiki report.</summary>
        public static readonly List<SynapseTestRecord> Records = new List<SynapseTestRecord>();

        public static void RunAll(IEnumerable<SynapseTestCase> cases)
        {
            Passed = Failed = Skipped = 0;
            Records.Clear();

            foreach (var test in cases)
            {
                if (test == null || test.Run == null) continue;

                string skip = null;
                try { skip = test.SkipReason?.Invoke(); }
                catch (Exception ex) { skip = "skip predicate threw: " + ex.Message; }

                if (skip != null)
                {
                    Skipped++;
                    SynapseTestReporter.Skip(test.Name, skip);
                    Record(test, "SKIP", skip);
                    continue;
                }

                try
                {
                    var detail = test.Run();
                    Passed++;
                    SynapseTestReporter.Pass(test.Name, detail ?? "ok");
                    Record(test, "PASS", detail ?? "ok");
                }
                catch (SynapseTestFailure fail)
                {
                    Failed++;
                    SynapseTestReporter.Fail(test.Name, fail.Message);
                    Record(test, "FAIL", fail.Message);
                }
                catch (Exception ex)
                {
                    // An unexpected throw is a failure, not a crash — keep running the rest.
                    Failed++;
                    SynapseTestReporter.Fail(test.Name, $"unexpected {ex.GetType().Name}: {ex.Message}");
                    Record(test, "FAIL", $"unexpected {ex.GetType().Name}: {ex.Message}");
                }
            }

            SynapseTestReporter.Summary(Passed, Failed, Skipped);
            SynapseTestReport.Write(Records, Passed, Failed, Skipped);
        }

        private static void Record(SynapseTestCase test, string outcome, string detail)
        {
            if (string.IsNullOrEmpty(test.Tier)) return; // only tiered cases go in the report
            Records.Add(new SynapseTestRecord
            {
                Name = test.Name,
                Tier = test.Tier,
                Polarity = test.Polarity,
                Scenario = test.Scenario,
                Expectation = test.Expectation,
                Outcome = outcome,
                Detail = detail
            });
        }
    }

    /// <summary>
    /// Renders the tiered test outcomes to a human-readable wiki page (Core/Learning/Test_Report.md,
    /// picked up by sync-wiki). Separates Determination (upstream / LLM decision) from Execution
    /// (downstream / deterministic mechanics), so a reader sees at a glance whether the model is
    /// deciding correctly versus whether the engine executes a decision correctly.
    /// </summary>
    public static class SynapseTestReport
    {
        public static void Write(List<SynapseTestRecord> records, int passed, int failed, int skipped)
        {
            var tiered = records?.Where(r => !string.IsNullOrEmpty(r.Tier)).ToList() ?? new List<SynapseTestRecord>();
            if (tiered.Count == 0) return;
            try
            {
                var core = LoadedModManager.RunningModsListForReading
                    .FirstOrDefault(m => m.PackageId != null && m.PackageId.ToLower() == "rimsynapse.core");
                if (core == null) { SynapseTestReporter.Info("Test report skipped: Core mod root not found."); return; }
                string dir = System.IO.Path.Combine(core.RootDir, "Learning");
                if (!System.IO.Directory.Exists(dir)) { SynapseTestReporter.Info($"Test report skipped: {dir} does not exist."); return; }
                string path = System.IO.Path.Combine(dir, "Test_Report.md");

                var sb = new StringBuilder();
                sb.AppendLine("# RimSynapse Test Report");
                sb.AppendLine();
                sb.AppendLine("Auto-generated by the in-game TestRunner. Cases split into two tiers:");
                sb.AppendLine();
                sb.AppendLine("- **Determination** (upstream) — does the LLM *decide* correctly (the assessment/script/memory it produces)? Runs against a live model; skipped under `-quicktest`.");
                sb.AppendLine("- **Execution** (downstream) — given a decision, does the engine *execute* it correctly? Deterministic, structured-field assertions.");
                sb.AppendLine();
                sb.AppendLine($"_Last run: **{passed} passed, {failed} failed, {skipped} skipped** — {System.DateTime.Now:yyyy-MM-dd HH:mm}._");

                foreach (var tier in new[] { "Determination", "Execution" })
                {
                    var rows = tiered.Where(r => r.Tier == tier).ToList();
                    if (rows.Count == 0) continue;
                    sb.AppendLine();
                    sb.AppendLine(tier == "Determination"
                        ? "## Determination — upstream (does the LLM decide correctly?)"
                        : "## Execution — downstream (does the engine execute a decision correctly?)");
                    sb.AppendLine();
                    sb.AppendLine("| Result | +/- | Scenario | Expectation | Detail |");
                    sb.AppendLine("|---|---|---|---|---|");
                    foreach (var r in rows)
                    {
                        // A case that passed but returned a "SKIPPED" detail is a self-skip (the suite's
                        // convention, since a real Skip is miscounted by the harness) — render it as SKIP.
                        string outcome = (r.Outcome == "PASS" && r.Detail != null && r.Detail.StartsWith("SKIPPED"))
                            ? "SKIP" : r.Outcome;
                        sb.AppendLine($"| {Mark(outcome)} | {r.Polarity} | {Esc(r.Scenario)} | {Esc(r.Expectation)} | {Esc(r.Detail)} |");
                    }
                }

                System.IO.File.WriteAllText(path, sb.ToString());
                SynapseTestReporter.Info($"Wrote test report ({tiered.Count} tiered case(s)) to {path}");
            }
            catch (Exception ex)
            {
                SynapseTestReporter.Info($"Could not write test report: {ex.Message}");
            }
        }

        private static string Mark(string outcome)
        {
            switch (outcome)
            {
                case "PASS": return "✅ PASS";
                case "FAIL": return "❌ FAIL";
                case "SKIP": return "⏭️ SKIP";
                default: return outcome;
            }
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
