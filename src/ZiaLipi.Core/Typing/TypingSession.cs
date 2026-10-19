// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using ZiaLipi.Core.Suggestions;

namespace ZiaLipi.Core.Typing;

/// <summary>
/// A change to apply to the text in the target application:
/// press Backspace <see cref="Backspaces"/> times, then type <see cref="Insert"/>.
/// </summary>
public readonly record struct TextEdit(int Backspaces, string Insert)
{
    public static readonly TextEdit None = new(0, "");
    public bool IsEmpty => Backspaces == 0 && Insert.Length == 0;
}

/// <summary>
/// The word currently being typed. Keeps the Roman input, the candidates and
/// what has already been typed into the target application, and works out
/// the minimal edit needed whenever the chosen Bangla text changes.
/// </summary>
public sealed class TypingSession(SuggestionBuilder builder)
{
    private string _buffer = "";
    private string _onScreen = "";
    private Suggestion? _suggestion;

    /// <summary>Roman text typed for the current word.</summary>
    public string Buffer => _buffer;
    public bool IsActive => _buffer.Length > 0;
    public IReadOnlyList<string> Candidates => _suggestion?.Words ?? [];
    public int SelectedIndex { get; private set; }
    public int MaxCandidates { get; set; } = 9;

    /// <summary>Characters that become part of a word (printable ASCII except space).</summary>
    public static bool IsInputChar(char c) => c >= '!' && c <= '~';

    public TextEdit Type(char c)
    {
        if (!IsInputChar(c))
            throw new ArgumentOutOfRangeException(nameof(c));
        _buffer += c;
        return Refresh();
    }

    public TextEdit Backspace()
    {
        if (!IsActive)
            return TextEdit.None;
        _buffer = _buffer[..^1];
        if (_buffer.Length > 0)
            return Refresh();
        var edit = ReplaceWith("");
        Reset();
        return edit;
    }

    /// <summary>Moves the highlighted candidate up/down (wrapping around).</summary>
    public TextEdit MoveSelection(int delta)
    {
        int count = Candidates.Count;
        if (count == 0)
            return TextEdit.None;
        return Select(((SelectedIndex + delta) % count + count) % count);
    }

    public TextEdit Select(int index)
    {
        if (_suggestion == null || index < 0 || index >= Candidates.Count)
            return TextEdit.None;
        SelectedIndex = index;
        builder.UpdateCandidateSelection(_buffer, _suggestion.RawWords[index]);
        return ReplaceWith(Candidates[index]);
    }

    /// <summary>Accepts the current word as typed and starts a new one.</summary>
    public void Commit()
    {
        if (IsActive && _suggestion != null)
            builder.StringCommitted(_buffer, _suggestion.RawWords[SelectedIndex]);
        Reset();
    }

    /// <summary>Forgets the current word without recording anything.</summary>
    public void Reset()
    {
        _buffer = "";
        _onScreen = "";
        _suggestion = null;
        SelectedIndex = 0;
    }

    private TextEdit Refresh()
    {
        var s = builder.Suggest(_buffer);
        if (s.Words.Count > MaxCandidates)
            s = s with { Words = [.. s.Words.Take(MaxCandidates)], RawWords = [.. s.RawWords.Take(MaxCandidates)] };
        _suggestion = s;
        SelectedIndex = Math.Clamp(s.PrevSelection, 0, s.Words.Count - 1);
        return ReplaceWith(s.Words[SelectedIndex]);
    }

    private TextEdit ReplaceWith(string target)
    {
        int common = 0;
        int max = Math.Min(_onScreen.Length, target.Length);
        while (common < max && _onScreen[common] == target[common])
            common++;
        var edit = new TextEdit(_onScreen.Length - common, target[common..]);
        _onScreen = target;
        return edit;
    }
}
