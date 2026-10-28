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
        result.Diagnostics[0].ToDiagnostic(null).Id.Should().Be("BP101");
        result.Diagnostics[0].ToDiagnostic(null).GetMessage().Should().Contain("multiple handlers");
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
        result.Diagnostics[0].ToDiagnostic(null).Id.Should().Be("BP101");
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
        result.Diagnostics[0].ToDiagnostic(null).Id.Should().Be("BP100");
        result.Diagnostics[0].ToDiagnostic(null).GetMessage().Should().Contain("does not have a handler method");
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

    [Fact]
    public void FindHandler_WithBaseClassProperties_DetectsPropertyInjections()
    {
        // Arrange — mirrors the S365 ApiOperations pattern
        var source = """
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using Blueprint.Authorization;
            using Microsoft.Extensions.Logging;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public abstract class ApiOperationsBase
            {
                public ApiOperationContext Context { get; set; }
                public IUserAuthorisationContext Identity { get; set; }
                public ILogger Logger { get; set; }
                public CancellationToken CancellationToken { get; set; }
            }

            public partial class TestQuery : IQuery<string> { }

            public class TestQueryHandler : ApiOperationsBase
            {
                public Task<string> HandleAsync(TestQuery query)
                {
                    return Task.FromResult("handled");
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(
            source,
            "TestQuery",
            [
                typeof(Blueprint.Runtime.Exec.ApiOperationContext).Assembly,
                typeof(Microsoft.Extensions.Logging.ILogger).Assembly,
            ]);

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.HandlerType.Should().Be(HandlerType.External);
        result.Value.PropertyInjections.Should().NotBeNull();

        var injections = result.Value.PropertyInjections!.Value.ToList();
        injections.Should().HaveCount(4);
        injections.Should().Contain(
            static p => p.PropertyName == "Context" && p.SourceExpression == "context");
        injections.Should().Contain(
            static p => p.PropertyName == "Identity" && p.SourceExpression == "context.UserAuthorisationContext!");
        injections.Should().Contain(
            static p => p.PropertyName == "Logger" && p.SourceExpression == "this.Logger");
        injections.Should().Contain(
            static p => p.PropertyName == "CancellationToken" && p.SourceExpression == "context.OperationCancelled");
    }

    [Fact]
    public void FindHandler_WithoutBaseClassProperties_HasNullPropertyInjections()
    {
        // Arrange — plain handler with no settable well-known properties
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
        result.Value.PropertyInjections.Should().BeNull();
    }

    [Fact]
    public void FindHandler_WithReadOnlyProperties_DoesNotDetectPropertyInjections()
    {
        // Arrange — read-only properties should be ignored
        var source = """
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public abstract class ReadOnlyBase
            {
                public ApiOperationContext Context { get; }
            }

            public partial class TestQuery : IQuery<string> { }

            public class TestQueryHandler : ReadOnlyBase
            {
                public Task<string> HandleAsync(TestQuery query)
                {
                    return Task.FromResult("handled");
                }
            }
            """;

        var (compilation, operationType) = CompileAndGetOperationType(
            source,
            "TestQuery",
            [typeof(Blueprint.Runtime.Exec.ApiOperationContext).Assembly]);

        // Act
        var result = HandlerDiscovery.FindHandler(operationType, compilation);

        // Assert
        result.Value.Should().NotBeNull();
        result.Value.PropertyInjections.Should().BeNull();
    }

    /// <summary>
    /// Helper method to compile source and get the operation type symbol.
    /// </summary>
    private static (Compilation Compilation, INamedTypeSymbol OperationType) CompileAndGetOperationType(
        string source,
        string typeName)
    {
        return CompileAndGetOperationType(source, typeName, additionalAssemblies: []);
    }

    private static (Compilation Compilation, INamedTypeSymbol OperationType) CompileAndGetOperationType(
        string source,
        string typeName,
        System.Reflection.Assembly[] additionalAssemblies)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));

        foreach (var asm in additionalAssemblies)
        {
            references.Add(MetadataReference.CreateFromFile(asm.Location));
        }

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
