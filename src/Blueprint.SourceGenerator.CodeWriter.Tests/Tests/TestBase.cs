using Microsoft.CodeAnalysis;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Blueprint.SourceGenerator.CodeWriter.Tests
{
    /// <summary>
    /// Provides the shared golden-file assertion and Roslyn symbol scaffolding used by the vendored CodeWriter tests.
    /// </summary>
    /// <remarks>
    /// The base class keeps legacy upstream-style tests concise while ensuring every builder suite compares emitted code
    /// against the same reviewed sample files.
    /// </remarks>
    public abstract class TestBase
    {
        /// <summary>
        /// Gets the namespace used by the reviewed sample-code fixtures.
        /// </summary>
        public const string Namespace = "Blueprint.SourceGenerator.CodeWriter.SampleCode";

        /// <summary>
        /// Gets the xUnit output sink used to print failed generated output before the golden-file assertion fires.
        /// </summary>
        protected ITestOutputHelper testOutputHelper { get; }

        /// <summary>
        /// Initialises the shared test harness state for a builder suite.
        /// </summary>
        /// <param name="testOutput">The xUnit output sink for the current test.</param>
        protected TestBase(ITestOutputHelper testOutput)
        {
            this.testOutputHelper = testOutput;
        }

        /// <summary>
        /// Compares an emitted enum against the reviewed sample file that defines its expected shape.
        /// </summary>
        /// <param name="builder">The enum builder whose emitted output should match the sample file.</param>
        protected void MakeAssertion(EnumBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        /// <summary>
        /// Compares an emitted class against the reviewed sample file that defines its expected shape.
        /// </summary>
        /// <param name="builder">The class builder whose emitted output should match the sample file.</param>
        protected void MakeAssertion(ClassBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        /// <summary>
        /// Compares an emitted record against the reviewed sample file that defines its expected shape.
        /// </summary>
        /// <param name="builder">The record builder whose emitted output should match the sample file.</param>
        protected void MakeAssertion(RecordBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        /// <summary>
        /// Compares an emitted delegate against the reviewed sample file that defines its expected shape.
        /// </summary>
        /// <param name="builder">The delegate builder whose emitted output should match the sample file.</param>
        protected void MakeAssertion(DelegateBuilder builder)
        {
            this.MakeAssertion(builder.Name, builder.Builder.Build());
        }

        /// <summary>
        /// Compares an emitted source file against the reviewed sample file for a custom file name.
        /// </summary>
        /// <param name="builder">The code builder whose emitted output should match the sample file.</param>
        /// <param name="fileName">The sample file name to compare against.</param>
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

        /// <summary>
        /// Creates a lightweight Roslyn named-type symbol stub for tests that only care about namespace and display-name
        /// behaviour.
        /// </summary>
        /// <param name="namespace">The namespace the fake symbol should report.</param>
        /// <param name="name">The type name the fake symbol should report.</param>
        /// <returns>A mocked named-type symbol that behaves like a simple Roslyn type reference.</returns>
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
