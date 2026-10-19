using System.Text;
using System.Text.Json;
using ZiaLipi.Core.Phonetic;
using ZiaLipi.Core.Suggestions;

namespace ZiaLipi.Core.Tests;

/// <summary>
/// Compares the C# engine with outputs recorded from the original JavaScript
/// engine (see tools/phonetic-data/convert.mjs).
/// </summary>
public class GoldenTests
{
    private sealed record TextCase(string Input, string Output);
    private sealed record SuggestCase(string Input, string[] Words, int PrevSelection);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static T Load<T>(string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name)), Options)!;

    [Fact]
    public void Phonetic_matches_original()
    {
        var failures = Load<List<TextCase>>("phonetic.json")
            .Select(c => (c, actual: PhoneticParser.Default.Parse(c.Input)))
            .Where(x => x.actual != x.c.Output)
            .Select(x => $"{x.c.Input}: expected {x.c.Output}, got {x.actual}")
            .ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Regex_matches_original()
    {
        var failures = Load<List<TextCase>>("regex.json")
            .Select(c => (c, actual: JsEscape(RegexBuilder.Default.Parse(c.Input))))
            .Where(x => x.actual != x.c.Output)
            .Select(x => $"{x.c.Input}: expected {x.c.Output}, got {x.actual}")
            .ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Suggestions_match_original()
    {
        var failures = new List<string>();
        foreach (var c in Load<List<SuggestCase>>("suggest.json"))
        {
            // Type the word one key at a time into a fresh builder, like the recorder did.
            var builder = new SuggestionBuilder(new CandidateSelections());
            Suggestion s = null!;
            for (int n = 1; n <= c.Input.Length; n++)
                s = builder.Suggest(c.Input[..n]);

            if (!s.Words.SequenceEqual(c.Words) || s.PrevSelection != c.PrevSelection)
                failures.Add($"{c.Input}: expected [{string.Join(",", c.Words)}]@{c.PrevSelection}, got [{string.Join(",", s.Words)}]@{s.PrevSelection}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // The JS regex builder writes non-Latin-1 chars as "\u0" + hex.
    private static string JsEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s)
            sb.Append(ch >= 255 ? "\\u0" + ((int)ch).ToString("x") : ch.ToString());
        return sb.ToString();
    }
}
