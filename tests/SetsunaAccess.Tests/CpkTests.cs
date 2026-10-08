using System.IO;
using SetsunaAccess;
using Xunit;

/// <summary>Runs against the installed game's parameter.cpk when it is present.</summary>
public class CpkTests
{
    private const string Archive = @"E:\modgames\setsuna\I.am.Setsuna\SETSUNA_Data\StreamingAssets\x86_64\parameter.cpk";
    private const string Extracted = @"E:\modgames\setsuna\mod\research\params";

    [Fact]
    public void ListsEveryFile()
    {
        if (!File.Exists(Archive)) return;
        var cpk = new Cpk(Archive);
        Assert.Equal(275, cpk.Count);
        Assert.True(cpk.Contains("ma_0001_01Placement"));
    }

    [Theory]
    [InlineData("ma_0001_01Placement")]
    [InlineData("FloorData")]
    [InlineData("EncryptionData")] // the one CRILAYLA-compressed file
    public void MatchesPythonExtraction(string name)
    {
        var reference = Path.Combine(Extracted, name);
        if (!File.Exists(Archive) || !File.Exists(reference)) return;
        Assert.Equal(File.ReadAllBytes(reference), new Cpk(Archive).Read(name));
    }

    [Fact]
    public void MissingFileIsNull()
    {
        if (!File.Exists(Archive)) return;
        Assert.Null(new Cpk(Archive).Read("NoSuchFile"));
    }
}
