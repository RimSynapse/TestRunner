using System.Collections.Generic;
using RimSynapse.WorldNews.Models;
using RimSynapse.WorldNews.Newspaper;

namespace RimSynapse.TestRunner
{
    /// <summary>
    /// Covers the expanded <see cref="NewspaperIssue"/> model and the single parse path
    /// (<see cref="SynapseNewspaperGenerator.ParseIssue"/>) that both renderers will read (WorldNews#21).
    ///
    /// <para>Structural, not a live-LLM test: it feeds canned responses through the same
    /// <c>ParseIssue</c> the generation callback uses, so the schema the suite validates and the schema
    /// the game consumes cannot drift. Asserts the full shape survives (masthead, lead + secondaries,
    /// sidebars, ads), that prose and assets stay separate (image prompt captured, no resolved asset on
    /// a fresh parse), and that a sparse response is normalized rather than throwing.</para>
    /// </summary>
    public static class WorldNewsIssueModelCases
    {
        private const string FullIssue = @"Here is your issue:
{
  ""PaperName"": ""The Evening Gazette"",
  ""Volume"": ""VOL. CXXXIV"",
  ""IssueNumber"": ""NO. 248"",
  ""Date"": ""5th of Jugust, 5502"",
  ""Price"": ""$1.50"",
  ""Headline"": ""COUNCIL PASSES STRICT NEW PARK RULES"",
  ""Stories"": [
    { ""Title"": ""Council Passes Park Rules"", ""Byline"": ""By David Chen"", ""Content"": ""After hours of testimony..."", ""ImagePrompt"": ""a packed frontier town-hall meeting"", ""ImageCaption"": ""Opponents voice concerns."" },
    { ""Title"": ""Main Street Resurfacing"", ""Content"": ""Reconstruction begins..."", ""ImagePrompt"": """" }
  ],
  ""Sidebars"": [ { ""Title"": ""Heatwave Advisory"", ""Content"": ""High pressure settles in."" } ],
  ""Ads"": [ { ""Advertiser"": ""ACME Hardware"", ""Copy"": ""Fresh stock of duct tape."" } ],
  ""SectionFooter"": ""SECTION A""
}";

        // Minimal: no masthead, no images, one story. Exercises ApplyMastheadDefaults + null-collection guards.
        private const string SparseIssue = @"{ ""Headline"": ""Quiet day"", ""Stories"": [ { ""Title"": ""Nothing much"", ""Content"": ""All was calm."" } ] }";

        public static IEnumerable<SynapseTestCase> All()
        {
            yield return new SynapseTestCase("WorldNews_IssueModelRoundTrips", () =>
            {
                NewspaperIssue issue = SynapseNewspaperGenerator.ParseIssue(FullIssue);
                Assert.NotNull(issue, "ParseIssue returned null for a well-formed issue (JSON extraction or deserialization failed)");

                Assert.True(issue.PaperName == "The Evening Gazette", $"PaperName not preserved: '{issue.PaperName}'");
                Assert.True(issue.Volume == "VOL. CXXXIV" && issue.IssueNumber == "NO. 248", "Masthead volume/number not preserved");
                Assert.True(issue.Stories != null && issue.Stories.Count == 2, $"Expected 2 stories, got {issue.Stories?.Count ?? 0}");

                var lead = issue.Stories[0];
                Assert.True(lead.Title == "Council Passes Park Rules", $"Lead title wrong: '{lead.Title}'");
                Assert.True(lead.WantsImage, "Lead story should carry an image prompt");
                Assert.False(lead.HasImage, "A freshly parsed story must have no resolved asset (prose and assets are separate)");
                Assert.False(issue.Stories[1].WantsImage, "Secondary with empty ImagePrompt should be text-only");

                Assert.True(issue.Sidebars.Count == 1 && issue.Ads.Count == 1, "Sidebar/ad not preserved");
                Assert.True(issue.SectionFooter == "SECTION A", "Section footer not preserved");

                return $"Round-tripped: {issue.Stories.Count} stories, lead image-wanted, {issue.Sidebars.Count} sidebar(s), {issue.Ads.Count} ad(s)";
            },
            tier: "Execution",
            polarity: "positive",
            scenario: "A full expanded-issue LLM response is parsed through ParseIssue",
            expectation: "Masthead, lead+secondary stories, sidebars and ads survive; image prompt captured, asset unresolved");

            yield return new SynapseTestCase("WorldNews_SparseIssueNormalized", () =>
            {
                NewspaperIssue issue = SynapseNewspaperGenerator.ParseIssue(SparseIssue);
                Assert.NotNull(issue, "ParseIssue returned null for a minimal but valid issue");

                Assert.True(!string.IsNullOrWhiteSpace(issue.PaperName), "PaperName should be defaulted when omitted");
                Assert.NotNull(issue.Sidebars, "Sidebars must be non-null after normalization");
                Assert.NotNull(issue.Ads, "Ads must be non-null after normalization");
                Assert.True(issue.Stories.Count == 1 && !issue.Stories[0].WantsImage, "Single text-only story expected");

                return $"Sparse issue normalized: paper='{issue.PaperName}', {issue.Stories.Count} story, no images";
            },
            tier: "Execution",
            polarity: "positive",
            scenario: "A minimal response omitting masthead, sidebars, ads and images is parsed",
            expectation: "Masthead is defaulted and collections are non-null; no exception");
        }
    }
}
