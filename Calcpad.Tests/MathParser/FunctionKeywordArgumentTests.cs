namespace Calcpad.Tests;

public class FunctionKeywordArgumentTests
{
    private static TestCalc NewCalc() => new(new());

    [Fact]
    public void OmittedOptionalArguments_UseDefaults()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2; z = 3) = x + y + z");
        Assert.Equal(6d, calc.Run("f(1)"));
    }

    [Fact]
    public void PositionalArgumentsOverrideDefaults()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2; z = 3) = x + y + z");
        Assert.Equal(111d, calc.Run("f(1; 10; 100)"));
    }

    [Fact]
    public void KeywordArguments_CanBeGivenInAnyOrder()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2; z = 3) = x + y + z");
        Assert.Equal(111d, calc.Run("f(z = 100; x = 1; y = 10)"));
    }

    [Fact]
    public void PositionalFollowedByKeyword_IsAllowed()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2; z = 3) = x + y + z");
        Assert.Equal(103d, calc.Run("f(1; z = 100)"));
    }

    [Fact]
    public void KeywordArgumentsWithoutSpaces_AreRecognized()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Equal(11d, calc.Run("f(x=1;y=10)"));
    }

    [Fact]
    public void DefaultsAreEvaluatedAtCallTime()
    {
        var calc = NewCalc();
        calc.Run("a = 5");
        calc.Run("f(x; y = a) = x + y");
        Assert.Equal(6d, calc.Run("f(1)"));
        calc.Run("a = 50");
        Assert.Equal(51d, calc.Run("f(1)"));
    }

    [Fact]
    public void DefaultExpression_CanContainFunctionCalls()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = sqr(16)) = x + y");
        Assert.Equal(5d, calc.Run("f(1)"));
    }

    [Fact]
    public void DefaultCanBeAVector()
    {
        var calc = NewCalc();
        calc.Run("f(x; v = [1; 2; 3]) = x*len(v)");
        Assert.Equal(6d, calc.Run("f(2)"));
    }

    [Fact]
    public void FunctionWithoutDefaults_IsUnaffected()
    {
        var calc = NewCalc();
        calc.Run("f(x; y) = x + y");
        Assert.Equal(3d, calc.Run("f(1; 2)"));
    }

    [Fact]
    public void NestedCalls_ResolveDefaults()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Equal(7d, calc.Run("f(f(1); 4)"));
    }

    [Fact]
    public void NestedCallInsideARewrittenArgumentList_IsAlsoResolved()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2; z = 3) = x + y + z");
        // The outer call is rewritten, so the inner one must be rewritten too
        Assert.Equal(13d, calc.Run("f(f(1); 4)"));
        Assert.Equal(6d, calc.Run("f(x = f(1; z = 0); y = 0)"));
    }

    [Fact]
    public void DefaultCallingAnotherFunctionWithDefaults()
    {
        var calc = NewCalc();
        calc.Run("g(a; b = 4) = a + b");
        calc.Run("f(x; y = g(1)) = x + y");
        Assert.Equal(6d, calc.Run("f(1)"));
    }

    [Fact]
    public void MissingRequiredArgument_Throws()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Throws<MathParserException>(() => calc.Run("f()"));
    }

    [Fact]
    public void DuplicateArgument_Throws()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Throws<MathParserException>(() => calc.Run("f(1; x = 3)"));
    }

    [Fact]
    public void RequiredParameterAfterOptional_Throws()
    {
        var calc = NewCalc();
        Assert.Throws<MathParserException>(() => calc.Run("f(x = 1; y) = x + y"));
    }

    [Fact]
    public void PositionalAfterKeyword_Throws()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Throws<MathParserException>(() => calc.Run("f(x = 1; 2)"));
    }

    [Fact]
    public void IndentedDefinition_IsNotRewritten()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        calc.Run("   f(x; y = 20) = x + y");
        Assert.Equal(21d, calc.Run("f(1)"));
    }

    [Fact]
    public void KeywordArgument_WithUnits()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2kg) = x + y");
        Assert.Equal(3d, calc.Run("f(1kg)"));
        Assert.Equal(6d, calc.Run("f(1kg; y = 5kg)"));
    }

    [Fact]
    public void Redefinition_KeepsWorking()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Equal(3d, calc.Run("f(1)"));
        calc.Run("f(x; y = 20) = x + y");
        Assert.Equal(21d, calc.Run("f(1)"));
    }
}
