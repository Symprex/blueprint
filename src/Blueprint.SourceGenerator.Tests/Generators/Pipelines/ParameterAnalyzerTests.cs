using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.Pipelines;

/// <summary>
/// Tests for the ParameterAnalyzer class, focusing on parameter resolution strategies.
/// </summary>
public class ParameterAnalyzerTests
{
    [Fact]
    public void AnalyzeParameters_WithOperationParameter_IdentifiesAsOperation()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string> { }

            public class TestQueryHandler
            {
                public string Handle(TestQuery query)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQueryHandler", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: true);

        // Assert - External handlers have the operation as first param, which is skipped in analysis
        parameters.Count.Should().Be(0);
    }

    [Fact]
    public void AnalyzeParameters_WithApiOperationContext_IdentifiesAsFromContext()
    {
        // Arrange
        var source = """
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle(ApiOperationContext context)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQuery", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: false);

        // Assert
        parameters.Count.Should().Be(1);
        parameters[0].ParameterName.Should().Be("context");
        parameters[0].Source.Should().Be(ParameterSource.FromContext);
        parameters[0].ParameterTypeName.Should().Contain("ApiOperationContext");
    }

    [Fact]
    public void AnalyzeParameters_WithBlueprintApp_IdentifiesAsFromContextProperty()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle(BlueprintApp app)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQuery", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: false);

        // Assert
        parameters.Count.Should().Be(1);
        parameters[0].ParameterName.Should().Be("app");
        parameters[0].Source.Should().Be(ParameterSource.FromContextProperty);
        parameters[0].ContextPropertyName.Should().Be("App");
    }

    [Fact]
    public void AnalyzeParameters_WithDataModelParameter_IdentifiesAsFromContextProperty()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle(BlueprintApp dataModel)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQuery", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: false);

        // Assert
        parameters.Count.Should().Be(1);
        parameters[0].ParameterName.Should().Be("dataModel");
        parameters[0].Source.Should().Be(ParameterSource.FromContextProperty);
        parameters[0].ContextPropertyName.Should().Be("App");
    }

    [Fact]
    public void AnalyzeParameters_WithCancellationToken_IdentifiesAsFromContextProperty()
    {
        // Arrange
        var source = """
            using Blueprint.Model;
            using System.Threading;

            namespace TestNamespace;

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle(CancellationToken cancellationToken)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQuery", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: false);

        // Assert
        parameters.Count.Should().Be(1);
        parameters[0].ParameterName.Should().Be("cancellationToken");
        parameters[0].Source.Should().Be(ParameterSource.FromContextProperty);
        parameters[0].ContextPropertyName.Should().Be("OperationCancelled");
    }

    [Fact]
    public void AnalyzeParameters_WithDependencyInjectionService_IdentifiesAsFromDI()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public interface IMyService { }

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle(IMyService service)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQuery", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: false);

        // Assert
        parameters.Count.Should().Be(1);
        parameters[0].ParameterName.Should().Be("service");
        parameters[0].Source.Should().Be(ParameterSource.FromDependencyInjection);
        parameters[0].ContextPropertyName.Should().BeNull();
    }

    [Fact]
    public void AnalyzeParameters_WithMultipleParameters_IdentifiesAllCorrectly()
    {
        // Arrange
        var source = """
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading;

            namespace TestNamespace;

            public interface IMyService { }

            public partial class TestQuery : IQuery<string>
            {
                public static string Handle(
                    ApiOperationContext context,
                    BlueprintApp app,
                    IMyService service,
                    CancellationToken token)
                {
                    return "test";
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestQuery", "Handle", "TestQuery");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: false);

        // Assert
        parameters.Count.Should().Be(4);

        parameters[0].ParameterName.Should().Be("context");
        parameters[0].Source.Should().Be(ParameterSource.FromContext);

        parameters[1].ParameterName.Should().Be("app");
        parameters[1].Source.Should().Be(ParameterSource.FromContextProperty);
        parameters[1].ContextPropertyName.Should().Be("App");

        parameters[2].ParameterName.Should().Be("service");
        parameters[2].Source.Should().Be(ParameterSource.FromDependencyInjection);

        parameters[3].ParameterName.Should().Be("token");
        parameters[3].Source.Should().Be(ParameterSource.FromContextProperty);
        parameters[3].ContextPropertyName.Should().Be("OperationCancelled");
    }

    [Fact]
    public void AnalyzeParameters_ForExternalHandler_SkipsFirstParameter()
    {
        // Arrange
        var source = """
            using Blueprint.Model;

            namespace TestNamespace;

            public interface IMyService { }

            public partial class TestCommand : ICommand { }

            public class TestCommandHandler
            {
                public void Handle(TestCommand command, IMyService service)
                {
                }
            }
            """;

        var (method, operationType) = GetHandlerMethodAndOperationType(source, "TestCommandHandler", "Handle", "TestCommand");

        // Act
        var parameters = ParameterAnalyzer.AnalyzeParameters(method, operationType, isExternalHandler: true);

        // Assert
        parameters.Count.Should().Be(1);
        parameters[0].ParameterName.Should().Be("service");
        parameters[0].Source.Should().Be(ParameterSource.FromDependencyInjection);
    }

    [Theory]
    [InlineData("Handle")]
    [InlineData("HandleAsync")]
    [InlineData("Invoke")]
    [InlineData("InvokeAsync")]
    public void IsHandlerMethodName_WithValidNames_ReturnsTrue(string methodName)
    {
        // Act & Assert
        ParameterAnalyzer.IsHandlerMethodName(methodName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Execute")]
    [InlineData("Process")]
    [InlineData("Run")]
    [InlineData("DoSomething")]
    public void IsHandlerMethodName_WithInvalidNames_ReturnsFalse(string methodName)
    {
        // Act & Assert
        ParameterAnalyzer.IsHandlerMethodName(methodName).Should().BeFalse();
    }

    /// <summary>
    /// Helper method to compile source and get handler method and operation type.
    /// </summary>
    private static (IMethodSymbol Method, INamedTypeSymbol OperationType) GetHandlerMethodAndOperationType(
        string source,
        string handlerTypeName,
        string methodName,
        string operationTypeName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Runtime.Exec.ApiOperationContext).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var handlerType = compilation.GetTypeByMetadataName($"TestNamespace.{handlerTypeName}");
        handlerType.Should().NotBeNull($"Type {handlerTypeName} should exist");

        var method = handlerType!.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        method.Should().NotBeNull($"Method {methodName} should exist on {handlerTypeName}");

        var operationType = compilation.GetTypeByMetadataName($"TestNamespace.{operationTypeName}");
        operationType.Should().NotBeNull($"Type {operationTypeName} should exist");

        return (method!, operationType!);
    }
}
