namespace Calcpad.Tests;

public class ExtensionMethodsTests
{
    public static TheoryData<string, string[]> EnumerateLinesCases => new()
    {
        { string.Empty, Array.Empty<string>() },
        { "a", ["a"] },
        { "a\n", ["a"] },
        { "a\r\n", ["a"] },
        { "a\r", ["a"] },
        { "a\nb", ["a", "b"] },
        { "a\rb", ["a", "b"] },
        { "a\nb\n", ["a", "b"] },
        { "a\r\nb\r\n", ["a", "b"] },
        { "a\rb\r", ["a", "b"] },
        { "a\n\nb\n", ["a", string.Empty, "b"] },
        { "a\nb\n\n", ["a", "b", string.Empty] },
        { "a\r\nb\r\n\r\n", ["a", "b", string.Empty] },
        { "a\rb\r\r", ["a", "b", string.Empty] },
        { "\n", [string.Empty] },
        { "\r\n", [string.Empty] },
        { "\r", [string.Empty] },
    };

    [Theory]
    [MemberData(nameof(EnumerateLinesCases))]
    public void EnumerateLines_TrimsSingleTerminalLineBreak_Symmetrically(string source, string[] expected)
    {
        Assert.Equal(expected, ToLineArray(source));
    }

    private static string[] ToLineArray(string source)
    {
        List<string> lines = [];
        foreach (var line in source.EnumerateLines())
            lines.Add(line.ToString());

        return [.. lines];
    }
}
