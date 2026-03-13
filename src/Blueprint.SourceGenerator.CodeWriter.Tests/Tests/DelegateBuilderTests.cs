using Xunit;
using Xunit.Abstractions;

namespace Blueprint.SourceGenerator.CodeWriter.Tests
{
    public class DelegateBuilderTests : TestBase
    {
        public DelegateBuilderTests(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        {
        }

        [Fact]
        public void GenerateInTopLevelNamespace()
        {
            var builder = CodeBuilder.Create(Namespace)
                .TopLevelNamespace()
                .AddDelegate("DelegateInTopLevelNamespace")
                .MakePublicDelegate();

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateWithGenericConstraints()
        {
            var builder = CodeBuilder.Create(Namespace)
                .TopLevelNamespace()
                .AddDelegate("DelegateWithGenericConstraint")
                .MakePublicDelegate()
                .AddGeneric("TView", x => x.AddConstraint("View"))
                .AddGeneric("TViewModel", x => x.AddConstraint("class"));

            this.MakeAssertion(builder);
        }

        [Fact]
        public void GenerateWithParameters()
        {
            var builder = CodeBuilder.Create(Namespace)
                .TopLevelNamespace()
                .AddDelegate("DelegateWithParameters")
                .MakePublicDelegate()
                .AddParameter("string", "message")
                .AddParameter("int", "number");

            this.MakeAssertion(builder);
        }
    }
}
