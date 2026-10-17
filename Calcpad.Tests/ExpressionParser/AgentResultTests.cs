using Calcpad.Core;

namespace Calcpad.Tests.AgentMode;

public class AgentResultTests
{
    [Fact]
    public void ScalarCaptureIsOptInAndDisabledByDefault()
    {
        var parser = new ExpressionParser { Settings = new Settings() };

        parser.Parse("x = 1", true, false);

        Assert.False(parser.CaptureScalarResults);
        Assert.Empty(parser.ScalarResults);
    }

    [Fact]
    public void DisabledCaptureStillAppliesUiOverridesWithoutCapturingInputResults()
    {
        var parser = new ExpressionParser
        {
            Settings = new Settings(),
            UiOverrides = new Dictionary<string, string> { ["x"] = "8" }
        };

        parser.Parse("#UI x = 4\ny = x^2", true, false);

        Assert.Empty(parser.Errors);
        Assert.Contains("x", parser.AppliedUiOverrides);
        Assert.Empty(parser.ScalarResults);
        Assert.Empty(parser.UiInputResults);
    }

    [Fact]
    public void CompositeUiReassignmentRemovesEarlierScalarInputSnapshot()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("#UI x = 1\n#UI x = [1; 2]", true, false);

        Assert.Empty(parser.Errors);
        Assert.False(parser.ScalarResults.ContainsKey("x"));
        Assert.False(parser.UiInputResults.ContainsKey("x"));
    }

    [Fact]
    public void CapturesScalarAssignmentsAsNumbersAndUnits()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("L = 6m\nA = L^2", true, false);

        Assert.Empty(parser.Errors);
        Assert.Equal(6, parser.ScalarResults["L"].Real);
        Assert.Equal(0, parser.ScalarResults["L"].Imaginary);
        Assert.False(parser.ScalarResults["L"].IsComplex);
        Assert.Equal("m", parser.ScalarResults["L"].Unit);
        Assert.Equal(36, parser.ScalarResults["A"].Real);
        Assert.NotNull(parser.ScalarResults["A"].Unit);
    }

    [Fact]
    public void CapturesStoredValueAfterTargetUnitConversion()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("x = 1m | cm", true, false);

        Assert.Empty(parser.Errors);
        Assert.Equal(100, parser.ScalarResults["x"].Real);
        Assert.Equal("cm", parser.ScalarResults["x"].Unit);
    }

    [Fact]
    public void UiOverridesAreCapturedAfterTheyAreApplied()
    {
        var parser = new ExpressionParser
        {
            Settings = new Settings(),
            CaptureScalarResults = true,
            UiOverrides = new Dictionary<string, string> { ["L"] = "8" }
        };

        parser.Parse("#UI L = 4m\nA = L^2", true, false);

        Assert.Empty(parser.Errors);
        Assert.Contains("L", parser.AppliedUiOverrides);
        Assert.Equal(8, parser.ScalarResults["L"].Real);
        Assert.Equal(64, parser.ScalarResults["A"].Real);
    }

    [Fact]
    public void UiInputResultKeepsTheAppliedInputValueAfterLaterReassignment()
    {
        var parser = new ExpressionParser
        {
            Settings = new Settings(),
            CaptureScalarResults = true,
            UiOverrides = new Dictionary<string, string> { ["x"] = "2" }
        };

        parser.Parse("#UI x = 1\ny = x^2\nx = 100", true, false);

        Assert.Empty(parser.Errors);
        Assert.Equal(2, parser.UiInputResults["x"].Real);
        Assert.Equal(100, parser.ScalarResults["x"].Real);
        Assert.Equal(4, parser.ScalarResults["y"].Real);
    }

    [Fact]
    public void CapturesComplexAssignmentsWithoutStringFormatting()
    {
        var settings = new Settings();
        settings.Math.IsComplex = true;
        var parser = new ExpressionParser { Settings = settings, CaptureScalarResults = true };

        parser.Parse("z = 1 + 2i", true, false);

        Assert.Empty(parser.Errors);
        Assert.Equal(1, parser.ScalarResults["z"].Real);
        Assert.Equal(2, parser.ScalarResults["z"].Imaginary);
        Assert.True(parser.ScalarResults["z"].IsComplex);
    }

    [Fact]
    public void PureImaginaryScalarIsReportedAsComplex()
    {
        var settings = new Settings();
        settings.Math.IsComplex = true;
        var parser = new ExpressionParser { Settings = settings, CaptureScalarResults = true };

        parser.Parse("z = 2i", true, false);

        Assert.Empty(parser.Errors);
        Assert.Equal(0, parser.ScalarResults["z"].Real);
        Assert.Equal(2, parser.ScalarResults["z"].Imaginary);
        Assert.True(parser.ScalarResults["z"].IsComplex);
    }

    [Fact]
    public void KeepsLastAssignmentAndSkipsCompositeValues()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("x = 1\nx = 3\nx = [1; 2]", true, false);

        Assert.False(parser.ScalarResults.ContainsKey("x"));
    }

    [Fact]
    public void CapturesFinalScalarValueFromCachedLoopAssignments()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("#for i = 1 : 4\nx = i\n#loop", true, false);

        Assert.Empty(parser.Errors);
        Assert.Equal(4, parser.ScalarResults["x"].Real);
    }

    [Fact]
    public void FunctionDefinitionAfterScalarAssignmentDoesNotAddSnapshot()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("x = 3\nf(x) = x + 1", true, false);

        Assert.Empty(parser.Errors);
        Assert.Single(parser.ScalarResults);
        Assert.Equal(3, parser.ScalarResults["x"].Real);
    }

    [Fact]
    public void IndexedCompositeAssignmentDoesNotCaptureScalarRightHandSide()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };

        parser.Parse("v = [1; 2]\nv[1] = 4", true, false);

        Assert.Empty(parser.Errors);
        Assert.False(parser.ScalarResults.ContainsKey("v"));
    }

    [Fact]
    public void ResetsCapturedValuesForEachParse()
    {
        var parser = new ExpressionParser { Settings = new Settings(), CaptureScalarResults = true };
        parser.Parse("old = 1", true, false);

        parser.Parse("current = 2", true, false);

        Assert.False(parser.ScalarResults.ContainsKey("old"));
        Assert.Equal(2, parser.ScalarResults["current"].Real);
    }

    [Fact]
    public void ParseResetsAppliedUiOverrides()
    {
        var parser = new ExpressionParser
        {
            Settings = new Settings(),
            CaptureScalarResults = true,
            UiOverrides = new Dictionary<string, string> { ["L"] = "8" }
        };
        parser.Parse("#UI L = 4m", true, false);
        Assert.Contains("L", parser.AppliedUiOverrides);

        parser.Parse("x = 1", true, false);

        Assert.Empty(parser.AppliedUiOverrides);
        Assert.False(parser.ScalarResults.ContainsKey("L"));
        Assert.Empty(parser.UiInputResults);
    }

    [Fact]
    public void DoesNotTreatARegularAssignmentAsAnAppliedUiOverride()
    {
        var parser = new ExpressionParser
        {
            Settings = new Settings(),
            CaptureScalarResults = true,
            UiOverrides = new Dictionary<string, string> { ["x"] = "8" }
        };

        parser.Parse("x = 4", true, false);

        Assert.Equal(4, parser.ScalarResults["x"].Real);
        Assert.DoesNotContain("x", parser.AppliedUiOverrides);
    }
}
