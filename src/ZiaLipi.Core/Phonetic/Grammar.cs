// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Ported from jsAvroPhonetic (ibus-avro). Copyright (C) OmicronLab.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZiaLipi.Core.Phonetic;

/// <summary>A set of find/replace patterns with context rules (phonetic grammar format).</summary>
internal sealed class Grammar
{
    [JsonPropertyName("patterns")] public List<Pattern> Patterns { get; set; } = [];
    [JsonPropertyName("vowel")] public string Vowel { get; set; } = "";
    [JsonPropertyName("consonant")] public string Consonant { get; set; } = "";
    [JsonPropertyName("casesensitive")] public string CaseSensitive { get; set; } = "";
    [JsonPropertyName("ignore")] public string Ignore { get; set; } = "";

    public static Grammar Load(Stream json)
    {
        var grammar = JsonSerializer.Deserialize<Grammar>(json)
            ?? throw new InvalidDataException("Empty grammar");
        foreach (var match in grammar.Patterns.SelectMany(p => p.Rules).SelectMany(r => r.Matches))
            match.Normalize();
        return grammar;
    }
}

internal sealed class Pattern
{
    [JsonPropertyName("find")] public string Find { get; set; } = "";
    [JsonPropertyName("replace")] public string Replace { get; set; } = "";
    [JsonPropertyName("rules")] public List<Rule> Rules { get; set; } = [];
}

internal sealed class Rule
{
    [JsonPropertyName("matches")] public List<Match> Matches { get; set; } = [];
    [JsonPropertyName("replace")] public string Replace { get; set; } = "";
}

internal sealed class Match
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";

    [JsonIgnore] public bool IsSuffix { get; private set; }
    [JsonIgnore] public bool Negative { get; private set; }

    /// <summary>Splits a leading '!' off the scope, as the JS engine does lazily.</summary>
    internal void Normalize()
    {
        IsSuffix = Type == "suffix";
        if (Scope.StartsWith('!'))
        {
            Negative = true;
            Scope = Scope[1..];
        }
    }
}
