using Xunit;
using Xunit.Abstractions;

namespace Blueprint.SourceGenerator.CodeWriter.Tests
{
    public class MethodTests : TestBase
    {
        public MethodTests(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        {
        }

        [Fact]
        public void GenerateAbstractMethod()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("SampleAbstractMethod")
                .AddMethod("MyAbstractMethod")
                .Abstract()
                .Class;

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateMethodWithPrimitiveParameter()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("SamplePrimitiveParameter")
                .AddMethod("MyPrimitiveMethod")
                .MakePublicMethod()
                .AddParameter(this.GetSymbol("System", "String"), "myParameter")
                .WithBody(x => x.AppendLine(@"Console.WriteLine(""Hello {0}"", myParameter);"))
                .Class;

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateMultipleMethodsInClass()
        {
            var builder = CodeBuilder.Create(Namespace)
                .AddClass("MultipleMethodClass")
                .AddMethod("Foo")
                .MakePublicMethod()
                .Class
                .AddMethod("Bar")
                .MakePublicMethod()
                .Class;

            this.MakeAssertion(builder);
        }
    }
}
