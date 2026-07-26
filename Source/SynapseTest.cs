using System;
using System.Collections.Generic;
using Verse;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// A single test case. <see cref="Run"/> throws <see cref="SynapseTestFailure"/> to fail;
    /// returning normally is a pass. The optional returned string is logged as pass detail.
    /// </summary>
    public class SynapseTestCase
    {
        public string Name;
        public Func<string> Run;

        /// <summary>Skip predicate — return a reason to skip, or null to run.</summary>
        public Func<string> SkipReason;

        public SynapseTestCase(string name, Func<string> run, Func<string> skipReason = null)
        {
            Name = name;
            Run = run;
            SkipReason = skipReason;
        }
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

        public static void RunAll(IEnumerable<SynapseTestCase> cases)
        {
            Passed = Failed = Skipped = 0;

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
                    continue;
                }

                try
                {
                    var detail = test.Run();
                    Passed++;
                    SynapseTestReporter.Pass(test.Name, detail ?? "ok");
                }
                catch (SynapseTestFailure fail)
                {
                    Failed++;
                    SynapseTestReporter.Fail(test.Name, fail.Message);
                }
                catch (Exception ex)
                {
                    // An unexpected throw is a failure, not a crash — keep running the rest.
                    Failed++;
                    SynapseTestReporter.Fail(test.Name, $"unexpected {ex.GetType().Name}: {ex.Message}");
                }
            }

            SynapseTestReporter.Summary(Passed, Failed, Skipped);
        }
    }
}
