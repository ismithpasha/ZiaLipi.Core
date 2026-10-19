// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
// Ported from ibus-avro (suggestionbuilder.js). Copyright (C) OmicronLab.

using System.Text.RegularExpressions;
using ZiaLipi.Core.Dictionary;
using ZiaLipi.Core.Phonetic;

namespace ZiaLipi.Core.Suggestions;

/// <param name="Words">Candidates to show, including converted leading/trailing punctuation.</param>
/// <param name="RawWords">The same candidates without the punctuation padding.</param>
/// <param name="PrevSelection">Index of the candidate to pre-select.</param>
public sealed record Suggestion(IReadOnlyList<string> Words, IReadOnlyList<string> RawWords, int PrevSelection);

/// <summary>
/// Builds the candidate list for a Roman word: autocorrect, dictionary words
/// (with Bangla suffixes), and the plain phonetic conversion.
/// </summary>
public sealed partial class SuggestionBuilder
{
    private const char Ya = 'য়';        // য়
    private const char Khandatta = 'ৎ'; // ৎ
    private const char Ta = 'ত';        // ত
    private const char Anusvara = 'ং';  // ং
    private const char Nga = 'ঙ';       // ঙ
    private const int MaxPhoneticCacheEntries = 20000;

    private readonly PhoneticParser _phonetic;
    private readonly DictionarySearch _dictionary;
    private readonly Dictionary<string, string> _autocorrect;
    private readonly Dictionary<string, string> _suffixes;
    private readonly CandidateSelections _selections;

    // Dictionary results per lower-cased Roman word, filled while the user types.
    private readonly Dictionary<string, List<string>> _phoneticCache = [];

    // The same for the extension's words, kept apart so they never mix into the base results.
    private readonly Dictionary<string, List<string>> _extraCache = [];
    private ISuggestionExtension? _extension;

    // Suffixed candidate → (base word, Roman base) for the last suggestion.
    private readonly Dictionary<string, (string Base, string Roman)> _tempCache = [];

    public SuggestionBuilder(CandidateSelections selections)
        : this(PhoneticParser.Default, DictionarySearch.Default, DataFiles.LoadMap("autocorrect.json"),
               DataFiles.LoadMap("suffix.json"), selections)
    {
    }

    internal SuggestionBuilder(PhoneticParser phonetic, DictionarySearch dictionary,
        Dictionary<string, string> autocorrect, Dictionary<string, string> suffixes, CandidateSelections selections)
    {
        _phonetic = phonetic;
        _dictionary = dictionary;
        _autocorrect = autocorrect;
        _suffixes = suffixes;
        _selections = selections;
    }

    /// <summary>When false only the plain phonetic conversion is returned.</summary>
    public bool DictionaryEnabled { get; set; } = true;

    /// <summary>More words and/or another candidate order; null = base dictionary only.</summary>
    public ISuggestionExtension? Extension
    {
        get => _extension;
        set
        {
            _extension = value;
            _extraCache.Clear(); // those words came from the previous extension
        }
    }

    public Suggestion Suggest(string word)
    {
        var (begin, middle, end) = SeparatePadding(word);
        begin = _phonetic.Parse(begin);
        end = _phonetic.Parse(end);
        string phonetic = _phonetic.Parse(middle);

        if (!DictionaryEnabled)
            return new Suggestion([begin + phonetic + end], [phonetic], 0);

        var dictSuggestion = GetDictionarySuggestion(middle);
        var (corrected, exact) = GetAutocorrect(word, middle);

        var words = new List<string>();

        // 1st: autocorrect
        if (corrected != null)
        {
            words.Add(corrected);
            if (!exact)
                dictSuggestion.Add(corrected); // so suffixes apply to it too
        }

        // 2nd: dictionary words, closest to the phonetic spelling first
        string key = middle.ToLowerInvariant();
        Remember(_phoneticCache, key, dictSuggestion);
        _tempCache.Clear();
        var baseCandidates = AddSuffix(middle, _phoneticCache);
        List<string> extraCandidates = [];
        if (_extension is { } extension)
        {
            Remember(_extraCache, key, _extraCache.TryGetValue(key, out var cachedExtra) ? cachedExtra : extension.FindWords(key));
            extraCandidates = AddSuffix(middle, _extraCache);
        }
        // Default, like the original: the base dictionary's list, unchanged, then
        // the extension's words after it (so they only add, never reorder).
        var ordered = _extension?.Order(baseCandidates, extraCandidates, phonetic)
            ?? [.. ByDistance(baseCandidates, phonetic), .. ByDistance(extraCandidates, phonetic)];
        foreach (var w in ordered)
            AddUnique(words, w);

        // 3rd: classic phonetic
        AddUnique(words, phonetic);

        int prevSelection = GetPreviousSelection(middle, words);

        var padded = new List<string>(words.Count);
        foreach (var w in words)
            padded.Add(exact && w == corrected ? w : begin + w + end);

        return new Suggestion(padded, words, prevSelection);
    }

    /// <summary>Call when the user moves the selection to another candidate.</summary>
    public void UpdateCandidateSelection(string word, string rawCandidate)
    {
        if (!DictionaryEnabled)
            return;
        _selections.Set(SeparatePadding(word).Middle, rawCandidate);
    }

    /// <summary>Call when a word is committed (space, enter, punctuation…).</summary>
    public void StringCommitted(string word, string rawCandidate)
    {
        if (!DictionaryEnabled)
            return;
        // Also remember the selection for the word without its suffix.
        if (_tempCache.TryGetValue(rawCandidate, out var entry) && !_selections.Contains(entry.Roman))
            _selections.Set(entry.Roman, entry.Base);
        _selections.Committed();
    }

    private List<string> GetDictionarySuggestion(string middle)
    {
        string key = middle.ToLowerInvariant();
        return _phoneticCache.TryGetValue(key, out var cached) ? [.. cached] : _dictionary.Search(key);
    }

    private static void Remember(Dictionary<string, List<string>> cache, string key, List<string> results)
    {
        if (cache.ContainsKey(key) || results.Count == 0)
            return;
        if (cache.Count >= MaxPhoneticCacheEntries)
            cache.Clear();
        cache[key] = [.. results];
    }

    /// <summary>Stable ordering, so equally close words keep the dictionary's order.</summary>
    private static IEnumerable<string> ByDistance(List<string> candidates, string phonetic) =>
        candidates.OrderBy(w => Levenshtein.Distance(phonetic, w));

    private (string? Corrected, bool Exact) GetAutocorrect(string word, string middle)
    {
        if (_autocorrect.TryGetValue(word, out var value) && value.Length > 0)
        {
            // Smileys etc. map to themselves and are kept as typed.
            return value == word ? (word, true) : (_phonetic.Parse(value), false);
        }
        if (_autocorrect.TryGetValue(_phonetic.FixString(middle), out value) && value.Length > 0)
            return (_phonetic.Parse(value), false);
        return (null, false);
    }

    private List<string> AddSuffix(string middle, Dictionary<string, List<string>> cache)
    {
        string word = middle.ToLowerInvariant();
        int len = word.Length;
        var list = cache.TryGetValue(word, out var cached) ? new List<string>(cached) : [];

        if (len < 2)
            return list;

        for (int j = 1; j <= len; j++)
        {
            string testSuffix = word[j..];
            if (!_suffixes.TryGetValue(testSuffix, out var suffix) || suffix.Length == 0)
                continue;
            string key = word[..(len - testSuffix.Length)];
            if (!cache.TryGetValue(key, out var items))
                continue;

            foreach (var item in items)
            {
                string full = JoinSuffix(item, suffix, out bool remember);
                list.Add(full);
                if (remember)
                    _tempCache.TryAdd(full, (item, key));
            }
        }
        return list;
    }

    private int GetPreviousSelection(string middle, List<string> words)
    {
        int len = middle.Length;
        if (!_selections.TryGet(middle, out var selected))
        {
            selected = "";
            // Not found: try the word without a suffix, e.g. "deshe" → "desh" + "e".
            for (int j = 1; j < len; j++)
            {
                string testSuffix = middle[(len - j)..].ToLowerInvariant();
                if (!_suffixes.TryGetValue(testSuffix, out var suffix) || suffix.Length == 0)
                    continue;
                string key = middle[..(len - j)];
                if (_selections.TryGet(key, out var keyWord))
                {
                    selected = JoinSuffix(keyWord, suffix, out _);
                    _selections.Set(middle, selected);
                    break;
                }
            }
        }
        int index = words.IndexOf(selected);
        return index < 0 ? 0 : index;
    }

    /// <summary>Appends a Bangla suffix to a word, fixing up the joint.</summary>
    private static string JoinSuffix(string word, string suffix, out bool remember)
    {
        remember = true;
        char right = word.Length > 0 ? word[^1] : '\0';
        if (IsVowel(right) && IsKar(suffix[0]))
            return word + Ya + suffix;
        if (right == Khandatta)
            return word[..^1] + Ta + suffix;
        if (right == Anusvara)
        {
            remember = false; // the original does not record these
            return word[..^1] + Nga + suffix;
        }
        return word + suffix;
    }

    private static bool IsKar(char c) => "ািীুূৃেৈোৌৄ".Contains(c);

    private static bool IsVowel(char c) => "অআইঈউঊঋএঐওঔঌৡািীুূৃেৈোৌ".Contains(c);

    private static void AddUnique(List<string> list, string item)
    {
        if (!list.Contains(item))
            list.Add(item);
    }

    internal static (string Begin, string Middle, string End) SeparatePadding(string word)
    {
        var m = PaddingRegex().Match(word);
        return (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
    }

    // Splits leading/trailing punctuation from the word (same expression as the original).
    [GeneratedRegex(@"(^(?::`|\.`|[\-\]~!@#%&*()_=+[{}'"";<>\/?|.,])*?(?=(?:,{2,}))|^(?::`|\.`|[\-\]~!@#%&*()_=+[{}'"";<>\/?|.,])*)(.*?(?:,,)*)((?::`|\.`|[\-\]~!@#%&*()_=+[{}'"";<>\/?|.,])*$)", RegexOptions.CultureInvariant)]
    private static partial Regex PaddingRegex();
}
