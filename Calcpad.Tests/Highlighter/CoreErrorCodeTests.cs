using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Calcpad.Highlighter.Linter.Constants;

namespace Calcpad.Tests.Highlighter
{
    /// <summary>Every code Core reports must be in the linter's catalog, so LintIgnore accepts it.</summary>
    public class CoreErrorCodeTests
    {
        private static object Default(Type t) =>
            t == typeof(string) ? "x" : t == typeof(char) ? 'x' : Activator.CreateInstance(t);

        [Fact]
        public void EveryExceptionFactory_HasACatalogedCode()
        {
            var factories = typeof(MathParserException).Assembly.GetType("Calcpad.Core.Exceptions")!
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.ReturnType == typeof(MathParserException))
                .ToArray();
            Assert.True(factories.Length > 100);
            foreach (var factory in factories)
            {
                var ex = (MathParserException)factory.Invoke(null, factory.GetParameters().Select(p => Default(p.ParameterType)).ToArray())!;
                Assert.True(ErrorCodes.Descriptions.ContainsKey(ex.Code ?? ""), $"{factory.Name}: {ex.Code}");
            }
        }

        [Fact]
        public void EveryCodeLiteralInCore_IsCataloged()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(dir.FullName, "Calcpad.Core")))
                dir = dir.Parent!;

            var codes = Directory.GetFiles(Path.Combine(dir.FullName, "Calcpad.Core"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"(CPD-\\d{4})\"").Select(m => m.Groups[1].Value))
                .Distinct()
                .ToArray();
            Assert.NotEmpty(codes);
            Assert.All(codes, code => Assert.True(ErrorCodes.Descriptions.ContainsKey(code), code));
        }

        [Fact]
        public void RunErrors_CarryCode()
        {
            var parser = new ExpressionParser { Debug = true };
            parser.Parse("a = b + 1\nc = 1m + 1s\n#else", true, false);
            Assert.Equal(["CPD-3301", "CPD-5422", "CPD-3105"], parser.Errors.Select(e => e.Code));
        }

        [Fact]
        public void AssignmentAfterNonName_IsNotTheBuiltInNameCode()
        {
            var parser = new ExpressionParser { Debug = true };
            parser.Parse("2 = 3", true, false);
            Assert.Equal("CPD-5122", Assert.Single(parser.Errors).Code);
        }

        [Theory]
        [InlineData("#def m$\n#def n$\n#end def\n#end def", "CPD-2207")]
        [InlineData("#def m$ = 1\n#def m$ = 2", "CPD-2201")]
        [InlineData("#end def", "CPD-2206")]
        [InlineData("#def m$(a$) = a$\nx = m$(1; 2)", "CPD-3304")]
        [InlineData("#def m$ = 1\nx = q$", "CPD-3303")]
        public void MacroErrors_CarryCode(string code, string expected)
        {
            var parser = new MacroParser();
            parser.Parse(code, out _, null, 0, true);
            Assert.Contains(expected, parser.Errors.Select(e => e.Code));
        }
    }
}
