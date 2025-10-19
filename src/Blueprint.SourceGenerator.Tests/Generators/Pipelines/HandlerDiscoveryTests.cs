using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.Pipelines;

/// <summary>
/// Tests for the HandlerDiscovery class, focusing on finding inline and external handlers.
/// </summary>
public class HandlerDiscoveryTests
{
    [Fact]
    public void FindHandler_WithInlineStaticHandler_ReturnsInlineHandlerModel()
    {
        // Arrange
        var source = """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static Task<string> HandleAsync()
                {
                    return Task.FromResult("test");
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.HandlerType.Should().Be(HandlerType.Inline);
        result.Value.IsHandlerStatic.Should().BeTrue();
        result.Value.HandlerMethodName.Should().Be("HandleAsync");
        result.Value.IsHandlerAsync.Should().BeTrue();
        result.Value.OperationTypeName.Should().Be("TestQuery");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void FindHandler_WithInlineInstanceHandler_ReturnsInlineHandlerModel()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestCommand : ICommand<int>
            {
                public int Invoke()
                {
                    return 42;
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestCommand");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.HandlerType.Should().Be(HandlerType.Inline);
        result.Value.IsHandlerStatic.Should().BeFalse();
        result.Value.HandlerMethodName.Should().Be("Invoke");
        result.Value.IsHandlerAsync.Should().BeFalse();
        result.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Handle")]
    [InlineData("HandleAsync")]
    [InlineData("Invoke")]
    [InlineData("InvokeAsync")]
    public void FindHandler_WithAllValidHandlerMethodNames_FindsHandler(string methodName)
    {
        // Arrange
        var source = $$"""
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestOperation : IQuery<string>
            {
                public static string {{methodName}}()
                {
                    return "test";
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestOperation");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.HandlerMethodName.Should().Be(methodName);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void FindHandler_WithExternalHandler_ReturnsExternalHandlerModel()
    {
        // Arrange
        var source = """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string> { }

            public class TestQueryHandler
            {
                public Task<string> HandleAsync(TestQuery query)
                {
                    return Task.FromResult("handled");
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.HandlerType.Should().Be(HandlerType.External);
        result.Value.HandlerClassName.Should().Be("TestQueryHandler");
        result.Value.HandlerMethodName.Should().Be("HandleAsync");
        result.Value.IsHandlerStatic.Should().BeFalse();
        result.Value.IsHandlerAsync.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void FindHandler_WithMultipleInlineHandlers_ReturnsDiagnostic()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle()
                {
                    return "one";
                }

                public static string Invoke()
                {
                    return "two";
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().BeNull();
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BP101");
        result.Diagnostics[0].ToDiagnostic().GetMessage().Should().Contain("multiple handlers");
    }

    [Fact]
    public void FindHandler_WithMultipleExternalHandlers_ReturnsDiagnostic()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string> { }

            public class FirstHandler
            {
                public string Handle(TestQuery query) => "first";
            }

            public class SecondHandler
            {
                public string Invoke(TestQuery query) => "second";
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().BeNull();
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BP101");
    }

    [Fact]
    public void FindHandler_WithNoHandler_ReturnsDiagnostic()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                // No handler method
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().BeNull();
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BP100");
        result.Diagnostics[0].ToDiagnostic().GetMessage().Should().Contain("does not have a handler method");
    }

    [Fact]
    public void FindHandler_PrefersInlineOverExternal_WhenBothExist()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle() => "inline";
            }

            public class TestQueryHandler
            {
                public string Handle(TestQuery query) => "external";
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.HandlerType.Should().Be(HandlerType.Inline);
        result.Value.HandlerMethodName.Should().Be("Handle");
        result.Diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// Helper method to compile source and get the operation type symbol.
    /// </summary>
    private static (Compilation Compilation, INamedTypeSymbol OperationType) CompileAndGetOperationType(
        string source,
        string typeName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var operationType = compilation.GetTypeByMetadataName($"TestNamespace.{typeName}");

        operationType.Should().NotBeNull($"Type {typeName} should exist in compilation");

        return (compilation, operationType!);
    }
}
