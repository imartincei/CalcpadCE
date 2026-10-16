namespace Calcpad.Tests;

[Collection("CwdMutating")]
public class IncludeResolutionTests
{
    [Fact]
    public void DirectSelfInclude_ReportsCircularError_DoesNotOverflow()
    {
        using var temp = new TempDir();
        temp.Write("a.cpd", "#include a.cpd\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f) };
        macroParser.Parse("#include a.cpd\n", out var expanded, null, 0, false);

        Assert.Contains("ircular", expanded);
    }

    [Fact]
    public void MutualCircularInclude_ReportsCircularError_DoesNotOverflow()
    {
        using var temp = new TempDir();
        temp.Write("a.cpd", "#include b.cpd\n");
        temp.Write("b.cpd", "#include a.cpd\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f) };
        macroParser.Parse("#include a.cpd\n", out var expanded, null, 0, false);

        Assert.Contains("ircular", expanded);
    }

    [Fact]
    public void SiblingInclude_ExpandsCleanly()
    {
        using var temp = new TempDir();
        temp.Write("a.cpd", "#include b.cpd\n");
        temp.Write("b.cpd", "leaf = 1\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f) };
        macroParser.Parse("#include a.cpd\n", out var expanded, null, 0, false);

        Assert.DoesNotContain("ircular", expanded);
        Assert.DoesNotContain("not found", expanded);
        Assert.Contains("leaf = 1", expanded);
    }

    [Fact]
    public void NestedRelativeInclude_ResolvesAgainstParentFileDirectory()
    {
        using var temp = new TempDir();
        temp.Write("main.cpd", "#include lib/helper.cpd\n");
        temp.WriteSub("lib", "helper.cpd", "#include sibling.cpd\n");
        temp.WriteSub("lib", "sibling.cpd", "leaf = 1\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f) };
        macroParser.Parse("#include main.cpd\n", out var expanded, null, 0, false);

        Assert.DoesNotContain("not found", expanded);
        Assert.Contains("leaf = 1", expanded);
    }

    [Fact]
    public void AbsoluteInclude_ToFileOutsideMainsTree_ResolvesAndChainsRelative()
    {
        using var temp = new TempDir();
        var externalPath = temp.WriteSub("external", "x.cpd", "#include y.cpd\n");
        temp.WriteSub("external", "y.cpd", "leaf = outside\n");
        var mainPath = temp.WriteSub("project/sub", "main.cpd", $"#include {externalPath}\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f) };
        macroParser.Parse($"#include {mainPath}\n", out var expanded, null, 0, false);

        Assert.DoesNotContain("not found", expanded);
        Assert.Contains("leaf = outside", expanded);
    }

    [Fact]
    public void DeepInclude_RelativeAndAbsoluteAndRelativeAgain_ResolveCorrectly()
    {
        using var temp = new TempDir();
        temp.Write("main.cpd", "#include lvl1/a.cpd\n");
        temp.WriteSub("lvl1", "a.cpd", "#include lvl2/b.cpd\n");
        temp.WriteSub("lvl1/lvl2", "b.cpd", "#include lvl3/c.cpd\n");
        temp.WriteSub("lvl1/lvl2/lvl3", "c.cpd", "#include lvl4/d.cpd\n");
        var cousinPath = temp.WriteSub("lvl1/lvl2/other", "e.cpd", "#include f.cpd\n");
        temp.WriteSub("lvl1/lvl2/other", "f.cpd", "leaf = chain\n");
        temp.WriteSub("lvl1/lvl2/lvl3/lvl4", "d.cpd", $"#include {cousinPath}\n");

        var macroParser = new MacroParser { Include = (f, _) => File.ReadAllText(f) };
        macroParser.Parse("#include main.cpd\n", out var expanded, null, 0, false);

        Assert.DoesNotContain("not found", expanded);
        Assert.Contains("leaf = chain", expanded);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _previousCwd;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
        public TempDir()
        {
            Directory.CreateDirectory(Path);
            _previousCwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path);
        }
        public string Write(string name, string content)
        {
            var p = System.IO.Path.Combine(Path, name);
            File.WriteAllText(p, content);
            return p;
        }
        public string WriteSub(string sub, string name, string content)
        {
            var dir = System.IO.Path.Combine(Path, sub);
            Directory.CreateDirectory(dir);
            var p = System.IO.Path.Combine(dir, name);
            File.WriteAllText(p, content);
            return p;
        }
        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousCwd);
            Directory.Delete(Path, recursive: true);
        }
    }
}
