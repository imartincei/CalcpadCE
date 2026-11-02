using Calcpad.Core;

namespace Calcpad.Tests;

public class FileMetadataTests
{
    private static readonly Dictionary<string, string> Patch = new()
    {
        ["baseId"] = "b0000000-0000-0000-0000-000000000001",
        ["id"] = "10000000-0000-0000-0000-000000000002",
        ["version"] = "3"
    };

    [Fact]
    public void WithFileInfo_WithNoFooter_AppendsAnchorAndFileInfo()
    {
        var text = FileMetadata.WithFileInfo("a = 1\n", Patch);

        Assert.Equal(
            "a = 1\n" + FileMetadata.Anchor + "\n" +
            "'<!--{\"fileInfo\":{\"baseId\":\"b0000000-0000-0000-0000-000000000001\",\"id\":\"10000000-0000-0000-0000-000000000002\",\"version\":\"3\"}}-->\n",
            text);
    }

    [Fact]
    public void WithFileInfo_WithAFooter_MergesIntoIt()
    {
        var source = "a = 1\r\n" + FileMetadata.Anchor + "\r\n'<!--{\"fileInfo\":{\"title\":\"Beam\",\"id\":\"old\"}}-->";
        var text = FileMetadata.WithFileInfo(source, Patch);

        Assert.Equal(3, text.Split("\r\n").Length);
        var info = FileMetadata.TryReadFileInfo(text);
        Assert.Equal("Beam", FileMetadata.GetString(info, "title"));
        Assert.Equal("10000000-0000-0000-0000-000000000002", FileMetadata.GetString(info, FileMetadata.IdKey));
        Assert.Equal(3, FileMetadata.GetVersion(info));
    }

    [Fact]
    public void WithFileInfo_AfterUiOverrides_KeepsFileInfoLast()
    {
        var source = "a = 1\n" + FileMetadata.Anchor + "\n'<!--{\"uiOverrides\":{\"L:1\":\"8\"}}-->\n";
        var lines = FileMetadata.WithFileInfo(source, Patch).TrimEnd('\n').Split('\n');

        Assert.Equal(FileMetadata.Anchor, lines[1]);
        Assert.Contains("uiOverrides", lines[2]);
        Assert.Contains("fileInfo", lines[3]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void TryReadFileInfo_ReadsAMultiLineComment()
    {
        var source = "a = 1\n'<!--{\"fileInfo\":{ _\n\"id\":\"x\"}}-->\n";
        Assert.Equal("x", FileMetadata.GetString(FileMetadata.TryReadFileInfo(source), FileMetadata.IdKey));
    }

    [Fact]
    public void TryReadFileInfo_WithoutAFooter_IsNull()
    {
        Assert.Null(FileMetadata.TryReadFileInfo("a = 1\n'<!--{\"desc\":\"x\"}-->\nb = 2\n"));
    }

    [Fact]
    public void Cpdz_RoundTripsTheFooter()
    {
        var bytes = FileMetadata.EncodeText(FileMetadata.WithFileInfo("a = 1\n", Patch), "x.cpdz", null);
        var info = FileMetadata.TryReadFileInfo(FileMetadata.DecodeText(bytes, "x.cpdz"));
        Assert.Equal("b0000000-0000-0000-0000-000000000001", FileMetadata.GetString(info, FileMetadata.BaseIdKey));
    }
}
