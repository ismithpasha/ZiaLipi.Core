// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace ZiaLipi.Core.Suggestions;

/// <summary>
/// Plugs another word list and/or a different candidate order into
/// <see cref="SuggestionBuilder"/> (see <see cref="SuggestionBuilder.Extension"/>).
/// </summary>
public interface ISuggestionExtension
{
    /// <summary>Words, beyond the base dictionary, that a lower-case Roman word could stand for.</summary>
    List<string> FindWords(string romanWord);

    /// <summary>
    /// The candidates in the order to show them, or null for the default:
    /// the base dictionary's candidates by spelling closeness (the original
    /// order), then the extension's candidates by spelling closeness.
    /// </summary>
    /// <param name="baseCandidates">Base dictionary words, including suffixed forms.</param>
    /// <param name="extensionCandidates">Words from <see cref="FindWords"/>, including suffixed forms.</param>
    /// <param name="phonetic">The plain phonetic conversion of what was typed.</param>
    /// <param name="romanWord">What was typed, without punctuation (case kept).</param>
    IEnumerable<string>? Order(IReadOnlyList<string> baseCandidates, IReadOnlyList<string> extensionCandidates, string phonetic, string romanWord);
}
