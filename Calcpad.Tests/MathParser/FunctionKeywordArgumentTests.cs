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
    public void VectorDefault_WithSemicolons()
    {
        var calc = NewCalc();
        calc.Run("f(x; v = [1; 2; 3]) = x + sum(v)");
        Assert.Equal(7d, calc.Run("f(1)"));
        Assert.Equal(31d, calc.Run("f(1; v = [10; 20])"));
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
    public void NestedCallInsideAResolvedArgumentList_IsAlsoResolved()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2; z = 3) = x + y + z");
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
    public void IndentedDefinition_IsNotTreatedAsACall()
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

    [Fact]
    public void Default_IsNotCapturedByCallerParameter()
    {
        var calc = NewCalc();
        calc.Run("a = 1");
        calc.Run("f(x; y = a) = x + y");
        calc.Run("g(a) = f(0)");
        Assert.Equal(1d, calc.Run("g(50)"));
    }

    [Fact]
    public void Default_CanReferenceEarlierParameter()
    {
        var calc = NewCalc();
        calc.Run("f(b; h = b) = b*h");
        Assert.Equal(9d, calc.Run("f(3)"));
        Assert.Equal(6d, calc.Run("f(3; h = 2)"));
    }

    [Fact]
    public void Default_CanReferenceVariableDefinedLater()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = b) = x + y");
        calc.Run("b = 7");
        Assert.Equal(8d, calc.Run("f(1)"));
    }

    [Fact]
    public void GlobalChangeInDefault_ClearsCallerCache()
    {
        var calc = NewCalc();
        calc.Run("a = 1");
        calc.Run("f(x; y = a) = x + y");
        calc.Run("g(x) = f(x)");
        Assert.Equal(2d, calc.Run("g(1)"));
        calc.Run("a = 100");
        Assert.Equal(101d, calc.Run("g(1)"));
    }

    [Fact]
    public void KeywordArguments_WorkWithoutDefaults()
    {
        var calc = NewCalc();
        calc.Run("f(x; y) = x - y");
        Assert.Equal(4d, calc.Run("f(y = 1; x = 5)"));
    }

    [Fact]
    public void UnknownKeywordArgument_Throws()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Throws<MathParserException>(() => calc.Run("f(1; z = 3)"));
    }

    [Fact]
    public void RecursiveDefault_Throws()
    {
        var calc = NewCalc();
        calc.Run("g(x) = x");
        calc.Run("f(x; y = g(x)) = x + y");
        Assert.Throws<MathParserException>(() => calc.Run("g(x) = f(x)"));
    }

    [Fact]
    public void DefaultsInsideSolver()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x - y");
        Assert.Equal(2d, calc.Run("$Root{f(x) = 0 @ x = 0 : 10}"), 6);
    }

    [Fact]
    public void EmptyTrailingArgument_Throws()
    {
        var calc = NewCalc();
        calc.Run("f(x; y = 2) = x + y");
        Assert.Throws<MathParserException>(() => calc.Run("f(1;)"));
    }
}
