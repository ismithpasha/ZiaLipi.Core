using ZiaLipi.Core.Suggestions;
using ZiaLipi.Core.Typing;

namespace ZiaLipi.Core.Tests;

public class TypingSessionTests
{
    /// <summary>Simulates the target application's text box.</summary>
    private sealed class FakeTextBox
    {
        public string Text { get; private set; } = "";

        public void Apply(TextEdit edit)
        {
            Assert.True(edit.Backspaces <= Text.Length);
            Text = Text[..^edit.Backspaces] + edit.Insert;
        }

        public void TypeLiteral(string s) => Text += s;
    }

    private static TypingSession NewSession(CandidateSelections? selections = null) =>
        new(new SuggestionBuilder(selections ?? new CandidateSelections()));

    private static void TypeWord(TypingSession session, FakeTextBox box, string roman)
    {
        foreach (char c in roman)
            box.Apply(session.Type(c));
    }

    [Fact]
    public void Typing_a_sentence_produces_bangla()
    {
        var session = NewSession();
        var box = new FakeTextBox();
        foreach (var word in "ami banglay gan gai".Split(' '))
        {
            TypeWord(session, box, word);
            session.Commit();
            box.TypeLiteral(" ");
        }
        Assert.Equal("আমি বাংলায় গান গাই ", box.Text);
    }

    [Fact]
    public void Edits_are_minimal()
    {
        var session = NewSession();
        Assert.Equal(new TextEdit(0, "ক"), session.Type('k'));
        // "ki" → "কি": only the vowel sign is added
        Assert.Equal(new TextEdit(0, "ি"), session.Type('i'));
    }

    [Fact]
    public void Backspace_removes_roman_letters_then_the_word()
    {
        var session = NewSession();
        var box = new FakeTextBox();
        box.TypeLiteral("x ");
        TypeWord(session, box, "ka");
        Assert.Equal("x কা", box.Text);

        box.Apply(session.Backspace());
        Assert.Equal("x ক", box.Text);
        box.Apply(session.Backspace());
        Assert.Equal("x ", box.Text);
        Assert.False(session.IsActive);
        Assert.Equal(TextEdit.None, session.Backspace());
    }

    [Fact]
    public void Chosen_candidate_is_remembered()
    {
        var selections = new CandidateSelections();
        var session = NewSession(selections);
        var box = new FakeTextBox();

        TypeWord(session, box, "tomar");
        int index = session.Candidates.ToList().IndexOf("তোমার");
        Assert.True(index > 0, "তোমার should be offered");
        box.Apply(session.Select(index));
        Assert.Equal("তোমার", box.Text);
        session.Commit();

        var box2 = new FakeTextBox();
        TypeWord(session, box2, "tomar");
        Assert.Equal("তোমার", box2.Text);
    }

    [Fact]
    public void Remembered_word_also_applies_with_suffix()
    {
        var session = NewSession();
        var box = new FakeTextBox();
        TypeWord(session, box, "tomar");
        box.Apply(session.Select(session.Candidates.ToList().IndexOf("তোমার")));
        session.Commit();

        var box2 = new FakeTextBox();
        TypeWord(session, box2, "tomare");
        Assert.Equal("তোমারে", box2.Text);
    }

    [Fact]
    public void Selection_wraps_around()
    {
        var session = NewSession();
        TypeWord(session, new FakeTextBox(), "ami");
        int count = session.Candidates.Count;
        Assert.True(count > 1);
        session.MoveSelection(-1);
        Assert.Equal(count - 1, session.SelectedIndex);
        session.MoveSelection(1);
        Assert.Equal(0, session.SelectedIndex);
    }

    [Fact]
    public void Punctuation_padding_is_kept()
    {
        var session = NewSession();
        var box = new FakeTextBox();
        TypeWord(session, box, "ami.");
        Assert.Equal("আমি।", box.Text);
    }

    [Fact]
    public void Dictionary_off_gives_plain_phonetic()
    {
        var builder = new SuggestionBuilder(new CandidateSelections()) { DictionaryEnabled = false };
        var session = new TypingSession(builder);
        var box = new FakeTextBox();
        TypeWord(session, box, "tomar");
        Assert.Equal("তমার", box.Text);
        Assert.Single(session.Candidates);
    }

    [Fact]
    public void Selections_persist_to_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "zialipi-test-" + Guid.NewGuid() + ".json");
        try
        {
            var session = NewSession(new CandidateSelections(path));
            TypeWord(session, new FakeTextBox(), "tomar");
            session.Select(session.Candidates.ToList().IndexOf("তোমার"));
            session.Commit();

            var reloaded = NewSession(new CandidateSelections(path));
            var box = new FakeTextBox();
            TypeWord(reloaded, box, "tomar");
            Assert.Equal("তোমার", box.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
