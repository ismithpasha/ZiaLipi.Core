// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Ported from jsAvroPhonetic (ibus-avro). Copyright (C) OmicronLab.

using System.Text;

namespace ZiaLipi.Core.Phonetic;

/// <summary>
/// Builds a regular expression matching every Bangla spelling a Roman word
/// could stand for (used for dictionary lookups).
/// </summary>
internal sealed class RegexBuilder
{
    // Optional ya-phala / ba-phala / ma-phala, hasanta, visarga or chandrabindu after each letter.
    private const string ReplaceSuffix = "(্[যবম])?(্?)([ঃঁ]?)";

    private readonly RuleEngine _engine;
    private readonly string _ignore;

    internal RegexBuilder(Grammar grammar)
    {
        _engine = new RuleEngine(grammar, ReplaceSuffix);
        _ignore = grammar.Ignore;
    }

    public static RegexBuilder Default => LazyDefault.Value;
    private static readonly Lazy<RegexBuilder> LazyDefault =
        new(() => new RegexBuilder(DataFiles.LoadGrammar("regex.json")));

    public string Parse(string input) => _engine.Convert(FixString(input));

    private string FixString(string input)
    {
        var fixedText = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            char lower = char.ToLowerInvariant(c);
            if (!_ignore.Contains(lower))
                fixedText.Append(lower);
        }
        return fixedText.ToString();
    }
}
