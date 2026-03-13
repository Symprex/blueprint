using Microsoft.CodeAnalysis;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Blueprint.SourceGenerator.CodeWriter.Tests
{
    public abstract class TestBase
    {
        public const string Namespace = "Blueprint.SourceGenerator.CodeWriter.SampleCode";

        protected ITestOutputHelper testOutputHelper { get; }

        protected TestBase(ITestOutputHelper testOutput)
        {
            this.testOutputHelper = testOutput;
        }

        protected void MakeAssertion(EnumBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        protected void MakeAssertion(ClassBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        protected void MakeAssertion(RecordBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        protected void MakeAssertion(DelegateBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        protected void MakeAssertion(CodeBuilder builder, string fileName)
        {
            this.MakeAssertion(fileName, builder.Build());
        }

        private void MakeAssertion(string name, string actual)
        {
            var file = File.ReadAllText(Path.Combine("SampleCode", $"{name}.cs"));
            this.testOutputHelper.WriteLine("*** ACTUAL OUTPUT ***");
            this.testOutputHelper.WriteLine(actual);

            Assert.Equal(file, actual, ignoreLineEndingDifferences: true);
        }

        protected INamedTypeSymbol GetSymbol(string @namespace, string name)
        {
            var typeSymbol = new Mock<INamedTypeSymbol>();
            var namespaceSymbol = new Mock<INamespaceSymbol>();
            namespaceSymbol.Setup(x => x.Name)
                .Returns(@namespace);
            namespaceSymbol.Setup(x => x.MetadataName)
                .Returns(@namespace);
            namespaceSymbol.Setup(x => x.ToString())
                .Returns(@namespace);
            typeSymbol.Setup(x => x.Name)
                .Returns(name);
            typeSymbol.Setup(x => x.MetadataName)
                .Returns(name);
            typeSymbol.Setup(x => x.ContainingNamespace)
                .Returns(namespaceSymbol.Object);
            typeSymbol.Setup(x => x.ToDisplayString(It.IsAny<SymbolDisplayFormat>()))
                .Returns($"{@namespace}.{name}");

            return typeSymbol.Object;
        }
    }
}
