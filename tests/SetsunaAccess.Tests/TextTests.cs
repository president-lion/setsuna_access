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

public class MoreStringsTests
{
    [Theory]
    [InlineData("Alpha1", "1")]
    [InlineData("LeftControl", "Left Control")]
    [InlineData("W", "W")]
    [InlineData("None", "not assigned")]
    public void KeyNames(string code, string spoken) => Assert.Equal(spoken, Strings.KeyName(code));

    [Fact] public void HudDamage() => Assert.Equal("Goblin 120 damage", Strings.Hud("DAMAGE_HP", "Goblin", "120"));
    [Fact] public void HudHeal() => Assert.Equal("Aqua recovers 50 HP", Strings.Hud("RECOVERY_HP", "Aqua", "50"));
    [Fact] public void HudStatusUsesGameWord() => Assert.Equal("Goblin Poison", Strings.Hud("DEBUFF", "Goblin", "Poison"));
    [Fact] public void HudKillWithoutText() => Assert.Equal("Goblin defeated", Strings.Hud("KILL", "Goblin", ""));

    [Fact] public void Directions()
    {
        Assert.Equal("up", Strings.Direction(0));
        Assert.Equal("down left", Strings.Direction(5));
        Assert.Equal("up", Strings.Direction(8));
    }

    [Fact] public void Distances()
    {
        Assert.Equal("close", Strings.Distance(1));
        Assert.Equal("7 meters", Strings.Distance(7));
    }

    [Fact] public void Exits()
    {
        Assert.Equal("Exit", Strings.Exit(""));
        Assert.Equal("Exit to Village", Strings.Exit("Village"));
    }
}

public class ExitDetailTests
{
    [Fact] public void AddsPeopleAndSavePoint() =>
        Assert.Equal("Exit to Nive: Innkeeper, Boy, save point",
            Strings.ExitDetail("Exit to Nive", new System.Collections.Generic.List<string> { "Innkeeper", "Boy" }, true));

    [Fact] public void AtMostThreePeople() =>
        Assert.Equal("Exit to Nive: A, B, C",
            Strings.ExitDetail("Exit to Nive", new System.Collections.Generic.List<string> { "A", "B", "C", "D" }, false));

    [Fact] public void NothingKnownKeepsLabel() =>
        Assert.Equal("Exit to Nive", Strings.ExitDetail("Exit to Nive", new System.Collections.Generic.List<string>(), false));
}
