// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Ported from jsAvroPhonetic (ibus-avro). Copyright (C) OmicronLab.

using System.Text;

namespace ZiaLipi.Core.Phonetic;

/// <summary>
/// The pattern matcher shared by the phonetic parser and the dictionary
/// regex builder. Behaviour (including edge cases) mirrors the JS original.
/// </summary>
internal sealed class RuleEngine
{
    private readonly string _vowel;
    private readonly string _consonant;
    private readonly string _replaceSuffix;

    // Patterns grouped by their first character, original priority order kept.
    private readonly Dictionary<char, Pattern[]> _byFirstChar;

    public RuleEngine(Grammar grammar, string replaceSuffix = "")
    {
        _vowel = grammar.Vowel;
        _consonant = grammar.Consonant;
        _replaceSuffix = replaceSuffix;
        _byFirstChar = grammar.Patterns
            .Where(p => p.Find.Length > 0)
            .GroupBy(p => p.Find[0])
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    public bool IsVowel(char c) => _vowel.Contains(char.ToLowerInvariant(c));
    public bool IsConsonant(char c) => _consonant.Contains(char.ToLowerInvariant(c));
    public bool IsPunctuation(char c) => !(IsVowel(c) || IsConsonant(c));

    public string Convert(string text)
    {
        var output = new StringBuilder(text.Length * 2);
        for (int cur = 0; cur < text.Length; ++cur)
        {
            bool matched = false;
            if (_byFirstChar.TryGetValue(text[cur], out var patterns))
            {
                foreach (var pattern in patterns)
                {
                    int start = cur, end = cur + pattern.Find.Length;
                    if (end > text.Length || string.CompareOrdinal(text, start, pattern.Find, 0, pattern.Find.Length) != 0)
                        continue;

                    string replace = pattern.Replace;
                    foreach (var rule in pattern.Rules)
                    {
                        if (RuleApplies(rule, text, start, end))
                        {
                            replace = rule.Replace;
                            break;
                        }
                    }
                    output.Append(replace).Append(_replaceSuffix);
                    cur = end - 1;
                    matched = true;
                    break;
                }
            }
            if (!matched)
                output.Append(text[cur]);
        }
        return output.ToString();
    }

    private bool RuleApplies(Rule rule, string text, int start, int end)
    {
        int prev = start - 1;
        foreach (var m in rule.Matches)
        {
            int chk = m.IsSuffix ? end : prev;
            bool ok;
            switch (m.Scope)
            {
                case "punctuation":
                    ok = (chk < 0 && !m.IsSuffix) || (chk >= text.Length && m.IsSuffix) || IsPunctuation(text[chk]);
                    break;
                case "vowel":
                    ok = ((chk >= 0 && !m.IsSuffix) || (chk < text.Length && m.IsSuffix)) && IsVowel(text[chk]);
                    break;
                case "consonant":
                    ok = ((chk >= 0 && !m.IsSuffix) || (chk < text.Length && m.IsSuffix)) && IsConsonant(text[chk]);
                    break;
                case "exact":
                    int s = m.IsSuffix ? end : start - m.Value.Length;
                    int e = m.IsSuffix ? end + m.Value.Length : start;
                    // Note: "e < Length" (not <=) is how the original behaves.
                    ok = s >= 0 && e < text.Length && string.CompareOrdinal(text, s, m.Value, 0, m.Value.Length) == 0;
                    break;
                default:
                    continue;
            }
            if (ok == m.Negative)
                return false;
        }
        return true;
    }
}
