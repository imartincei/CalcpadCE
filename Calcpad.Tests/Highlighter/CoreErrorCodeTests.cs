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

        [Theory]
        [InlineData("$Plot{x^2 @ x = 0}", "CPD-3406")]
        [InlineData("$Plot{x|x|x @ x = 0 : 1}", "CPD-5120")]
        [InlineData("$Plot{ @ x = 0 : 1}", "CPD-5113")]
        [InlineData("$Map{x*y @ x = 0 : 1}", "CPD-3406")]
        public void PlotSyntaxErrors_AreRaised(string code, string expected)
        {
            var parser = new ExpressionParser { Debug = true };
            parser.Parse(code, true, false);
            Assert.Equal(expected, Assert.Single(parser.Errors).Code);
        }

        [Fact]
        public void RunErrors_CarryCodeAndKind()
        {
            var parser = new ExpressionParser { Debug = true };
            parser.Parse("a = b + 1\nc = 1m + 1s\n#else", true, false);
            Assert.Equal(
                [("CPD-3301", CalcpadErrorKind.Names), ("CPD-5422", CalcpadErrorKind.Values), ("CPD-3105", CalcpadErrorKind.Structure)],
                parser.Errors.Select(e => (e.Code, e.Kind)));
        }
    }
}
