using System.Linq;

namespace Calcpad.Tests
{
    public class ErrorColumnTests
    {
        private static (int, int) Columns(string code)
        {
            var error = new ExpressionParser().Check(code).Single();
            return (error.StartColumn, error.EndColumn);
        }

        [Theory]
        [InlineData("'Text' a = 1 + * 2 'more' b = 3", 7, 18)]
        [InlineData("    q = 3 foo + 1", 10, 13)]
        [InlineData("u = 5badunit", 5, 12)]
        [InlineData("  w = 1 ¤ 2", 8, 9)]
        [InlineData("#if 1 + * 2\n#end if", 4, 11)]
        public void Columns_PointAtTheError(string code, int start, int end)
        {
            Assert.Equal((start, end), Columns(code));
        }

        [Fact]
        public void ContinuedLine_CoversTheWholeLine()
        {
            Assert.Equal((-1, -1), Columns("x = 1 + _\n* 2"));
        }

        [Fact]
        public void KeywordError_CoversTheWholeLine()
        {
            Assert.Equal((-1, -1), Columns("#for i 1 : 2\n#loop"));
        }
    }
}
