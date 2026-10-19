// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Ported from jsAvroPhonetic (ibus-avro). Copyright (C) OmicronLab.

using System.Text;

namespace ZiaLipi.Core.Phonetic;

/// <summary>Converts Roman (Banglish) text to Bangla using the phonetic rules.</summary>
public sealed class PhoneticParser
{
    private readonly RuleEngine _engine;
    private readonly string _caseSensitive;

    internal PhoneticParser(Grammar grammar)
    {
        _engine = new RuleEngine(grammar);
        _caseSensitive = grammar.CaseSensitive;
    }

    public static PhoneticParser Default => LazyDefault.Value;
    private static readonly Lazy<PhoneticParser> LazyDefault =
        new(() => new PhoneticParser(DataFiles.LoadGrammar("phonetic.json")));

    /// <summary>e.g. "ami banglay gan gai" → "আমি বাংলায় গান গাই".</summary>
    public string Parse(string input) => _engine.Convert(FixString(input));

    /// <summary>Lower-cases everything except the case-sensitive letters (o, i, u, d, g, j, n, r, s, t, y, z).</summary>
    public string FixString(string input)
    {
        var fixedText = new StringBuilder(input.Length);
        foreach (char c in input)
            fixedText.Append(_caseSensitive.Contains(char.ToLowerInvariant(c)) ? c : char.ToLowerInvariant(c));
        return fixedText.ToString();
    }
}
