using System.Diagnostics;
using Xunit.Abstractions;
using ZiaLipi.Core.Suggestions;

namespace ZiaLipi.Core.Tests;

/// <summary>Timing tests run alone, so other tests do not compete for the CPU.</summary>
[CollectionDefinition("Performance", DisableParallelization = true)]
public class PerformanceCollection;

[Collection("Performance")]
public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Suggest_is_fast_enough_for_keystrokes()
    {
        var builder = new SuggestionBuilder(new CandidateSelections());
        builder.Suggest("a"); // warm up (loads the data)

        long worst = 0, total = 0;
        int keys = 0;
        string worstInput = "";
        foreach (var word in new[] { "amarsonarbangla", "oikottan", "bishshobidyalOy", "obhiggota", "ekushefebruary", "shadhinota" })
        {
            for (int n = 1; n <= word.Length; n++)
            {
                var sw = Stopwatch.StartNew();
                builder.Suggest(word[..n]);
                long ms = sw.ElapsedMilliseconds;
                total += ms;
                keys++;
                if (ms > worst)
                {
                    worst = ms;
                    worstInput = word[..n];
                }
            }
        }
        output.WriteLine($"average {(double)total / keys:F1} ms, worst {worst} ms for '{worstInput}'");
        // Shared CI machines are slower and noisier than a user's PC.
        long limit = Environment.GetEnvironmentVariable("CI") == "true" ? 500 : 100;
        Assert.True(worst < limit, $"Suggest took {worst} ms for '{worstInput}'");
    }
}
