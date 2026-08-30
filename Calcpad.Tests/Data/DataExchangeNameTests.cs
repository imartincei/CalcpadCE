using Calcpad.Core;

namespace Calcpad.Tests;

/// <summary>
/// The name in <c>#read</c>/<c>#write</c>/<c>#append</c> becomes a variable, so it has to obey
/// the same naming rules as an assignment. Without the check it lands in the variable table
/// unvalidated, where nothing can reference it again.
/// </summary>
public class DataExchangeNameTests
{
    [Fact]
    public void ReadAcceptsAValidName()
    {
        using var temp = new DataDir();
        var html = temp.Run("#read v_max from data.csv type=V");

        Assert.DoesNotContain("class=\"err\"", html);
    }

    [Theory]
    [InlineData("a.b")]
    [InlineData("1abc")]
    [InlineData("a@b")]
    public void ReadRejectsAnInvalidName(string name)
    {
        using var temp = new DataDir();
        var html = temp.Run($"#read {name} from data.csv type=V");

        Assert.Contains($"Invalid variable name: \"{name}\".", html);
    }

    [Fact]
    public void WriteRejectsAnInvalidName()
    {
        using var temp = new DataDir();
        var html = temp.Run("#write a.b to out.csv");

        Assert.Contains("Invalid variable name: \"a.b\".", html);
    }

    [Fact]
    public void AppendRejectsAnInvalidName()
    {
        using var temp = new DataDir();
        var html = temp.Run("#append a.b to out.csv");

        Assert.Contains("Invalid variable name: \"a.b\".", html);
    }

    private sealed class DataDir : IDisposable
    {
        private readonly string _dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public DataDir()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(System.IO.Path.Combine(_dir, "data.csv"), "1,2,3\n");
        }

        public string Run(params string[] lines)
        {
            var parser = new ExpressionParser
            {
                SourceFilePath = System.IO.Path.Combine(_dir, "worksheet.cpd"),
                AllowDataWrite = true
            };
            parser.Parse(string.Join('\n', lines), true, false);
            return parser.HtmlResult;
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { /* best-effort cleanup */ }
        }
    }
}
