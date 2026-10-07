using System;
using SetsunaAccess;
using Xunit;

public class TextCleanTests
{
    [Fact] public void StripsRichTextAndJoinsLines() =>
        Assert.Equal("Hello there friend", TextClean.Clean("<color=#ff0000>Hello</color>\nthere  friend"));

    [Fact] public void StripsUnconvertedGameTags() =>
        Assert.Equal("Take the .", TextClean.Clean("Take the <ITEM=12>."));

    [Fact] public void DropsIconGlyphsButReadsSymbols() =>
        Assert.Equal("© 2016 Potion x 3", TextClean.Clean((char)65503 + " 2016 " + (char)65535 + "Potion" + (char)65508 + "3"));

    [Fact] public void EmptyIsEmpty() => Assert.Equal("", TextClean.Clean(null));
}

public class StringsTests
{
    [Fact] public void PositionIsOneBased() => Assert.Equal("Load Game, 2 of 2", Strings.Item("Load Game", 1, 2));
    [Fact] public void SingleItemHasNoPosition() => Assert.Equal("OK", Strings.Item("OK", 0, 1));
    [Fact] public void SpeakerPrefix() => Assert.Equal("Setsuna: Hi.", Strings.Line("Setsuna", "Hi."));
    [Fact] public void NoSpeaker() => Assert.Equal("Hi.", Strings.Line(null, "Hi."));
}

public class RepeatFilterTests
{
    [Fact] public void DropsQuickDuplicateOnly()
    {
        var f = new RepeatFilter(TimeSpan.FromMilliseconds(400));
        var t = DateTime.UtcNow;
        Assert.True(f.Allow("a", t));
        Assert.False(f.Allow("a", t.AddMilliseconds(100)));
        Assert.True(f.Allow("a", t.AddMilliseconds(600)));
        Assert.True(f.Allow("b", t.AddMilliseconds(610)));
    }
}
