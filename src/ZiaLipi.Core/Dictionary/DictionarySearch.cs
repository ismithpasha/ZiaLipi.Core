// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Ported from ibus-avro (dbsearch.js). Copyright (C) OmicronLab.

using System.IO.Compression;
using System.Text.RegularExpressions;
using ZiaLipi.Core.Phonetic;

namespace ZiaLipi.Core.Dictionary;

/// <summary>Finds dictionary words that a Roman word could be a spelling of.</summary>
public sealed class DictionarySearch
{
    // Which word tables to look in, by the first Roman letter typed.
    private static readonly Dictionary<char, string[]> TablesByFirstLetter = new()
    {
        ['a'] = ["a", "aa", "e", "oi", "o", "nya", "y"],
        ['b'] = ["b", "bh"],
        ['c'] = ["c", "ch", "k"],
        ['d'] = ["d", "dh", "dd", "ddh"],
        ['e'] = ["i", "ii", "e", "y"],
        ['f'] = ["ph"],
        ['g'] = ["g", "gh", "j"],
        ['h'] = ["h"],
        ['i'] = ["i", "ii", "y"],
        ['j'] = ["j", "jh", "z"],
        ['k'] = ["k", "kh"],
        ['l'] = ["l"],
        ['m'] = ["h", "m"],
        ['n'] = ["n", "nya", "nga", "nn"],
        ['o'] = ["a", "u", "uu", "oi", "o", "ou", "y"],
        ['p'] = ["p", "ph"],
        ['q'] = ["k"],
        ['r'] = ["rri", "h", "r", "rr", "rrh"],
        ['s'] = ["s", "sh", "ss"],
        ['t'] = ["t", "th", "tt", "tth", "khandatta"],
        ['u'] = ["u", "uu", "y"],
        ['v'] = ["bh"],
        ['w'] = ["o"],
        ['x'] = ["e", "k"],
        ['y'] = ["i", "y"],
        ['z'] = ["h", "j", "jh", "z"],
    };

    private readonly Dictionary<string, string[]> _tables;
    private readonly RegexBuilder _regex;

    // The base and extra searches for one key press use the same pattern. Kept
    // per thread: Regex objects must not be shared by threads that search at once.
    private sealed record CachedPattern(DictionarySearch Owner, string Roman, Regex Pattern);
    [ThreadStatic] private static CachedPattern? _lastPattern;

    internal DictionarySearch(Dictionary<string, string[]> tables, RegexBuilder regex)
    {
        _tables = tables;
        _regex = regex;
    }

    /// <summary>The built-in base dictionary.</summary>
    public static DictionarySearch Default => LazyDefault.Value;
    private static readonly Lazy<DictionarySearch> LazyDefault = new(() =>
    {
        using var reader = DataFiles.OpenGzipText("dictionary.txt.gz");
        return new DictionarySearch(ReadTables(reader), RegexBuilder.Default);
    });

    /// <summary>
    /// Another word list in the same format: gzipped UTF-8 text with "#w_…"
    /// table lines, each followed by that table's words, one per line.
    /// </summary>
    public static DictionarySearch Load(Stream gzipText)
    {
        using var reader = new StreamReader(new GZipStream(gzipText, CompressionMode.Decompress), System.Text.Encoding.UTF8);
        return new DictionarySearch(ReadTables(reader), RegexBuilder.Default);
    }

    public int WordCount => _tables.Values.Sum(t => t.Length);

    public IEnumerable<string> Words => _tables.Values.SelectMany(t => t);

    /// <summary>Words a Roman word could stand for, in the dictionary's order.</summary>
    public List<string> Search(string romanWord)
    {
        var results = new List<string>();
        if (_tables.Count == 0 || romanWord.Length == 0 ||
            !TablesByFirstLetter.TryGetValue(char.ToLowerInvariant(romanWord[0]), out var tableNames))
            return results;

        AddMatches(_tables, tableNames, PatternFor(romanWord), results);
        return results;
    }

    /// <summary>
    /// The DFA engine (NonBacktracking) checks the ~50,000 words of a table set
    /// several times faster than the default engine. These patterns only use
    /// alternation, groups and optional parts, so every engine gives the same answer.
    /// </summary>
    private Regex PatternFor(string romanWord)
    {
        var last = _lastPattern;
        if (last != null && last.Owner == this && last.Roman == romanWord)
            return last.Pattern;
        var pattern = new Regex("^" + _regex.Parse(romanWord) + "$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        _lastPattern = new CachedPattern(this, romanWord, pattern);
        return pattern;
    }

    private static void AddMatches(Dictionary<string, string[]> tables, string[] tableNames, Regex pattern, List<string> results)
    {
        foreach (var name in tableNames)
        {
            if (!tables.TryGetValue("w_" + name, out var words))
                continue;
            foreach (var word in words)
            {
                if (pattern.IsMatch(word))
                    results.Add(word);
            }
        }
    }

    private static Dictionary<string, string[]> ReadTables(StreamReader reader)
    {
        var tables = new Dictionary<string, string[]>();
        string? table = null;
        var words = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#'))
            {
                if (table != null) tables[table] = [.. words];
                table = line[1..];
                words.Clear();
            }
            else if (line.Length > 0)
            {
                words.Add(line);
            }
        }
        if (table != null) tables[table] = [.. words];
        return tables;
    }
}
