using System.IO.Compression;
using System.Text;
using Calcpad.Server.Services;

namespace Calcpad.Tests;

/// <summary>
/// A portable package is the worksheet as text with its references bundled beside it, so each
/// test writes a small tree, packs it, and reads the archive back: the files that came along,
/// and the paths the documents inside now use to reach them.
/// </summary>
public class PortablePackageTests
{
    [Fact]
    public void Layout_HoldsTheDocumentAndAFolderOfWhatItReferences()
    {
        using var tree = new Tree();
        tree.Write("media/logo.png", "png");
        tree.Write("data/loads.csv", "1,2\n");
        var zip = tree.Pack("""
            'A worksheet
            '<img src="./media/logo.png">
            #read L from ./data/loads.csv
            """);

        Assert.Equal(
            ["root.cpd", "root.cpd.refs/loads.csv", "root.cpd.refs/logo.png"],
            Names(zip));
    }

    [Fact]
    public void Root_ReachesEveryReferenceThroughTheRefsFolder()
    {
        using var tree = new Tree();
        tree.Write("media/logo.png", "png");
        tree.Write("data/loads.csv", "1,2\n");
        tree.Write("inc/lib.cpd", "a = 1\n");
        var zip = tree.Pack("""
            #include ./inc/lib.cpd
            #read L from ./data/loads.csv type=R sep=';'
            '<img src="media/logo.png">
            """);

        Assert.Equal("""
            #include root.cpd.refs/lib.cpd
            #read L from root.cpd.refs/loads.csv type=R sep=';'
            '<img src="root.cpd.refs/logo.png">
            """, Text(zip, "root.cpd"));
    }

    /// <summary>
    /// A nested <c>#include</c> is the one reference resolved against the file holding it, so it
    /// is bundled from there and reached as a sibling once both are in the folder.
    /// </summary>
    [Fact]
    public void NestedInclude_ResolvesAgainstItsOwnFolderAndIsReachedAsASibling()
    {
        using var tree = new Tree();
        tree.Write("inc/deep.cpd", "b = 2\n");
        tree.Write("inc/lib.cpd", "#include ./deep.cpd\n");
        var zip = tree.Pack("#include ./inc/lib.cpd\n");

        Assert.Equal(
            ["root.cpd", "root.cpd.refs/deep.cpd", "root.cpd.refs/lib.cpd"], Names(zip));
        Assert.Equal("#include deep.cpd\n", Text(zip, "root.cpd.refs/lib.cpd"));
    }

    /// <summary>
    /// The data and images of an included file are resolved from the <em>root</em> document once
    /// the includes are expanded into it, so they are bundled from there and reached from there
    /// too — the included copy names the refs folder even though it sits inside it.
    /// </summary>
    [Fact]
    public void AnIncludedFilesDataAndImages_AreResolvedFromTheRootDocument()
    {
        using var tree = new Tree();
        tree.Write("media/detail.png", "png");
        tree.Write("tables/fy.csv", "355\n");
        tree.Write("inc/lib.cpd", """
            '<img src="./media/detail.png">
            #read T from ./tables/fy.csv
            """);
        var zip = tree.Pack("#include ./inc/lib.cpd\n");

        Assert.Equal(
            ["root.cpd", "root.cpd.refs/detail.png", "root.cpd.refs/fy.csv", "root.cpd.refs/lib.cpd"],
            Names(zip));
        Assert.Equal("""
            '<img src="root.cpd.refs/detail.png">
            #read T from root.cpd.refs/fy.csv
            """, Text(zip, "root.cpd.refs/lib.cpd"));
    }

    [Fact]
    public void IncludeBackToTheDocument_ReachesBackOutOfTheFolder()
    {
        using var tree = new Tree();
        tree.Write("inc/lib.cpd", "#include ../root.cpd\n");
        var zip = tree.Pack("#include ./inc/lib.cpd\n");

        Assert.Equal("#include ../root.cpd\n", Text(zip, "root.cpd.refs/lib.cpd"));
    }

    [Fact]
    public void WriteAndAppend_AreLeftAsWritten()
    {
        using var tree = new Tree();
        tree.Write("out/results.csv", "old\n");
        var source = """
            #write R to ./out/results.csv
            #append R to ./out/results.csv
            """;
        var zip = tree.Pack(source);

        Assert.Equal(source, Text(zip, "root.cpd"));
        Assert.Equal(["root.cpd"], Names(zip));
    }

    [Fact]
    public void AnAbsoluteWriteTarget_IsLeftAsWrittenWithTheOptionOff()
    {
        using var tree = new Tree();
        var source = $"#write R to {tree.At("out/results.csv")}\n";
        var zip = tree.Pack(source, nextToWorksheet: false);

        Assert.Equal(source, Text(zip, "root.cpd"));
    }

    [Fact]
    public void AnAbsoluteWriteTarget_CollapsesToItsFilenameWithTheOptionOn()
    {
        using var tree = new Tree();
        var source = $"#write R to {tree.At("out/results.csv")}\n";
        var zip = tree.Pack(source, nextToWorksheet: true);

        Assert.Equal("#write R to results.csv\n", Text(zip, "root.cpd"));
    }

    [Fact]
    public void AnEnvironmentVariableWriteTarget_CollapsesToItsFilename()
    {
        // Environment.ExpandEnvironmentVariables always uses the %name% form, even on
        // platforms whose shells use $NAME — a variable this test sets itself, so the
        // rewrite is proved out without depending on what the host environment happens
        // to define.
        using var tree = new Tree();
        const string variable = "CALCPAD_TEST_OUTPUT_DIR";
        Environment.SetEnvironmentVariable(variable, tree.At("out"));
        try
        {
            var zip = tree.Pack($"#write R to %{variable}%/results.csv\n", nextToWorksheet: true);
            Assert.Equal("#write R to results.csv\n", Text(zip, "root.cpd"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void ARelativeWriteTarget_IsNeverRewritten()
    {
        using var tree = new Tree();
        var source = """
            #write R to ./out/results.csv
            #append S to results.csv
            """;
        var zip = tree.Pack(source, nextToWorksheet: true);

        Assert.Equal(source, Text(zip, "root.cpd"));
    }

    [Fact]
    public void AppendBehavesAsWrite_ForTheCollapseOption()
    {
        using var tree = new Tree();
        var source = $"#append R to {tree.At("out/results.csv")}\n";
        var zip = tree.Pack(source, nextToWorksheet: true);

        Assert.Equal("#append R to results.csv\n", Text(zip, "root.cpd"));
    }

    [Fact]
    public void TwoAbsoluteWriteTargetsSharingAFilename_AreRefused()
    {
        using var tree = new Tree();
        var a = tree.At("a/results.csv");
        var b = tree.At("b/results.csv");
        var result = tree.Build($"""
            #write A to {a}
            #write B to {b}
            """, nextToWorksheet: true);

        Assert.Null(result.Zip);
        var message = Assert.Single(result.Errors);
        Assert.Contains("results.csv", message);
        Assert.Contains(a, message);
        Assert.Contains(b, message);
    }

    [Fact]
    public void AnAbsoluteWriteTargetCollidingWithARelativeOne_IsRefused()
    {
        using var tree = new Tree();
        var absolute = tree.At("out/results.csv");
        var result = tree.Build($"""
            #write A to {absolute}
            #write B to results.csv
            """, nextToWorksheet: true);

        Assert.Null(result.Zip);
        var message = Assert.Single(result.Errors);
        Assert.Contains("results.csv", message);
    }

    [Fact]
    public void WriteThenAppendToTheSameAbsoluteFile_IsNotACollision()
    {
        using var tree = new Tree();
        var path = tree.At("out/results.csv");
        var zip = tree.Pack($"""
            #write R to {path}
            #append R to {path}
            """, nextToWorksheet: true);

        Assert.Equal("""
            #write R to results.csv
            #append R to results.csv
            """, Text(zip, "root.cpd"));
    }

    [Fact]
    public void AWriteInsideAnInclude_IsRewrittenInThePackedCopy()
    {
        using var tree = new Tree();
        var absolute = tree.At("out/results.csv");
        tree.Write("inc/lib.cpd", $"#write R to {absolute}\n");
        var zip = tree.Pack("#include ./inc/lib.cpd\n", nextToWorksheet: true);

        Assert.Equal("#write R to results.csv\n", Text(zip, "root.cpd.refs/lib.cpd"));
    }

    [Fact]
    public void ASheetRangeTypeSepAndComment_SurviveAWriteRewrite()
    {
        using var tree = new Tree();
        var absolute = tree.At("out/results.xlsx");
        var source = $"#write R to {absolute}@Sheet1!A1:B2 type=R sep=';' 'a comment\n";
        var zip = tree.Pack(source, nextToWorksheet: true);

        Assert.Equal("#write R to results.xlsx@Sheet1!A1:B2 type=R sep=';' 'a comment\n",
            Text(zip, "root.cpd"));
    }

    [Fact]
    public void RemoteAndInlineImages_AreLeftAsWritten()
    {
        using var tree = new Tree();
        var source = """
            '<img src="https://example.com/x.png">
            '<img src="data:image/png;base64,AAA">
            """;
        Assert.Equal(source, Text(tree.Pack(source), "root.cpd"));
    }

    [Fact]
    public void ACommentAndAFieldBlock_SurviveTheRewrite()
    {
        using var tree = new Tree();
        tree.Write("inc/lib.cpd", "a = 1\n");
        var zip = tree.Pack("#include ./inc/lib.cpd #{1;2} 'the library\n");

        Assert.Equal("#include root.cpd.refs/lib.cpd #{1;2} 'the library\n", Text(zip, "root.cpd"));
    }

    [Fact]
    public void TwoReferencesWithOneName_AreRefusedNamingBoth()
    {
        using var tree = new Tree();
        tree.Write("data/loads.csv", "1\n");
        tree.Write("archive/loads.csv", "2\n");
        var result = tree.Build("""
            #read A from ./data/loads.csv
            #read B from ./archive/loads.csv
            """);

        Assert.Null(result.Zip);
        var message = Assert.Single(result.Errors);
        Assert.Contains("loads.csv", message);
        Assert.Contains(tree.At("data/loads.csv"), message);
        Assert.Contains(tree.At("archive/loads.csv"), message);
    }

    [Fact]
    public void AReferenceThatCannotBeRead_IsRefused()
    {
        using var tree = new Tree();
        var result = tree.Build("""
            'line 1
            #read L from ./data/absent.csv
            """);

        Assert.Null(result.Zip);
        var message = Assert.Single(result.Errors);
        Assert.Contains("root.cpd, line 2", message);
        Assert.Contains("#read ./data/absent.csv", message);
    }

    /// <summary>The file holding the line is the one named, not the document it was reached from.</summary>
    [Fact]
    public void AReferenceMissingInsideAnInclude_NamesThatFile()
    {
        using var tree = new Tree();
        tree.Write("inc/lib.cpd", "a = 1\n#include ./absent.cpd\n");
        var result = tree.Build("#include ./inc/lib.cpd\n");

        Assert.Null(result.Zip);
        Assert.Contains("lib.cpd, line 2", Assert.Single(result.Errors));
    }

    [Fact]
    public void AnUnsavedDocument_IsRefused()
    {
        var result = PortablePackage.Build("a = 1\n", null);

        Assert.Null(result.Zip);
        Assert.Contains("saved", Assert.Single(result.Errors));
    }

    [Fact]
    public void ACycleTerminates()
    {
        using var tree = new Tree();
        tree.Write("inc/a.cpd", "#include ./b.cpd\n");
        tree.Write("inc/b.cpd", "#include ./a.cpd\n");
        var zip = tree.Pack("#include ./inc/a.cpd\n");

        Assert.Equal(["root.cpd", "root.cpd.refs/a.cpd", "root.cpd.refs/b.cpd"], Names(zip));
        Assert.Equal("#include b.cpd\n", Text(zip, "root.cpd.refs/a.cpd"));
        Assert.Equal("#include a.cpd\n", Text(zip, "root.cpd.refs/b.cpd"));
    }

    [Fact]
    public void LineEndingsAndAMissingFinalNewline_AreKept()
    {
        using var tree = new Tree();
        tree.Write("inc/lib.cpd", "a = 1\n");
        var zip = tree.Pack("'first\r\n#include ./inc/lib.cpd\r\n'last");

        Assert.Equal("'first\r\n#include root.cpd.refs/lib.cpd\r\n'last", Text(zip, "root.cpd"));
    }

    [Fact]
    public void AByteOrderMark_IsKeptAndDoesNotHideTheFirstLine()
    {
        using var tree = new Tree();
        tree.Write("inc/lib.cpd", "a = 1\n");
        var zip = tree.Pack("﻿#include ./inc/lib.cpd\n");

        Assert.Equal("﻿#include root.cpd.refs/lib.cpd\n", Text(zip, "root.cpd"));
    }

    [Fact]
    public void TheSameFileIncludedTwice_IsBundledOnce()
    {
        using var tree = new Tree();
        tree.Write("inc/lib.cpd", "a = 1\n");
        var zip = tree.Pack("#include ./inc/lib.cpd\n#include inc/lib.cpd\n");

        Assert.Equal(["root.cpd", "root.cpd.refs/lib.cpd"], Names(zip));
    }

    [Fact]
    public void AWorksheetReferencingNothing_IsStillPacked()
    {
        using var tree = new Tree();
        var zip = tree.Pack("a = 1\n");

        Assert.Equal(["root.cpd"], Names(zip));
    }

    private static List<string> Names(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return [.. archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }

    private static string Text(byte[] zip, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        // Byte order marks are left in place rather than consumed: whether one survived the
        // rewrite is one of the things being checked.
        using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false), false);
        return reader.ReadToEnd();
    }

    /// <summary>A folder holding <c>root.cpd</c> and whatever it refers to.</summary>
    private sealed class Tree : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public Tree() => Directory.CreateDirectory(_path);

        public string At(string relative) => Path.Combine(_path, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string relative, string text)
        {
            var target = At(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }

        /// <summary>
        /// Packs <paramref name="content"/> as the worksheet <c>root.cpd</c> of this folder,
        /// which is written to disk as well so a reference back to it resolves.
        /// </summary>
        public PortablePackage.Result Build(string content, bool nextToWorksheet = false)
        {
            Write("root.cpd", content);
            return PortablePackage.Build(content, At("root.cpd"), nextToWorksheet);
        }

        public byte[] Pack(string content, bool nextToWorksheet = false)
        {
            var result = Build(content, nextToWorksheet);
            Assert.Empty(result.Errors);
            Assert.NotNull(result.Zip);
            Assert.Equal("root.zip", result.Name);
            return result.Zip;
        }

        public void Dispose() => Directory.Delete(_path, true);
    }
}
