using Calcpad.Core;

namespace Calcpad.Tests
{
    public class ElementAccessTests
    {
        [Fact]
        [Trait("Category", "ElementAccess")]
        public void VectorElement()
        {
            var calc = new TestCalc(new());
            Assert.Equal(20d, calc.Run(["v = [10; 20; 30]", "v.2"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void VectorElementInExpression()
        {
            var calc = new TestCalc(new());
            Assert.Equal(40d, calc.Run(["v = [10; 20; 30]", "v.1 + v.3"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void VectorElementWithComputedIndex()
        {
            var calc = new TestCalc(new());
            Assert.Equal(30d, calc.Run(["v = [10; 20; 30]", "i = 1", "v.(i + 2)"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void VectorElementAssignment()
        {
            var calc = new TestCalc(new());
            Assert.Equal(99d, calc.Run(["v = [10; 20; 30]", "v.2 = 99", "v.2"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void MatrixElement()
        {
            var calc = new TestCalc(new());
            Assert.Equal(4d, calc.Run(["m = [1; 2 | 3; 4]", "m.(2; 2)"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void ElementAccessOnUndeclaredName()
        {
            var calc = new TestCalc(new());
            Assert.Equal(20d, calc.Run(["f(x) = x.2", "f([10; 20; 30])"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void DotIsNotANameChar()
        {
            var calc = new TestCalc(new());
            Assert.ThrowsAny<Exception>(() => calc.Run(["a = 5", "a.b = 2"]));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void DecimalLiteralIsUnaffected()
        {
            var calc = new TestCalc(new());
            Assert.Equal(3.5d, calc.Run("1.25 + 2.25"));
        }

        [Fact]
        [Trait("Category", "ElementAccess")]
        public void CustomUnitIsUnaffected()
        {
            var calc = new TestCalc(new());
            Assert.Equal(4096d, calc.Run([".bit = 1", ".byte = 8*bit", "2*256*byte/bit"]));
        }

        [Theory]
        [Trait("Category", "ElementAccess")]
        [InlineData("a1", true)]
        [InlineData("a_1", true)]
        [InlineData("a,b", true)]
        [InlineData("α₁", true)]
        [InlineData("a.b", false)]
        [InlineData("v.1", false)]
        public void ValidatorRejectsDottedNames(string name, bool expected) =>
            Assert.Equal(expected, Validator.IsVariable(name));
    }
}
