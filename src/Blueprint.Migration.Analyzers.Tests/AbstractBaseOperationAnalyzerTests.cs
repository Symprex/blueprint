using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="Analyzers.AbstractBaseOperationAnalyzer"/> which flags abstract
/// generic classes that directly implement <c>IQuery&lt;T&gt;</c> or <c>ICommand&lt;T&gt;</c>.
/// </summary>
public sealed class AbstractBaseOperationAnalyzerTests
{
    [Xunit.Fact]
    public async Task When_abstract_generic_class_implements_IQuery_then_reports_bpmig202()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class TenantOrderedPagedQuery<T> : Blueprint.Model.IQuery<T> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG202");
        var diagnostic = diagnostics.Single(static d => d.Id == "BPMIG202");
        diagnostic.GetMessage().Should().Contain("TenantOrderedPagedQuery");
        diagnostic.GetMessage().Should().Contain("IQuery");
    }

    [Xunit.Fact]
    public async Task When_abstract_generic_class_implements_ICommand_then_reports_bpmig202()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface ICommand<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class BatchCommand<T> : Blueprint.Model.ICommand<T> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG202");
        diagnostics.Single(static d => d.Id == "BPMIG202")
            .GetMessage().Should().Contain("BatchCommand");
    }

    [Xunit.Fact]
    public async Task When_concrete_class_implements_IQuery_then_no_bpmig202()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public class GetUsersQuery : Blueprint.Model.IQuery<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG202").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_abstract_non_generic_class_implements_IQuery_then_no_bpmig202()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class OrderedPagedQuery : Blueprint.Model.IQuery<string> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG202").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_abstract_generic_inherits_IQuery_from_base_then_no_bpmig202()
    {
        // Only direct interface declarations should trigger the diagnostic,
        // not inherited ones from a base class
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class BaseQuery<T> : Blueprint.Model.IQuery<T> { }
                public abstract class TenantQuery<T> : BaseQuery<T> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        // Should only report on BaseQuery (directly implements IQuery),
        // not on TenantQuery (inherits from BaseQuery)
        diagnostics.Where(static d => d.Id == "BPMIG202").Should().HaveCount(1);
        diagnostics.Single(static d => d.Id == "BPMIG202")
            .GetMessage().Should().Contain("BaseQuery");
    }

    [Xunit.Fact]
    public async Task When_abstract_generic_implements_unrelated_interface_then_no_bpmig202()
    {
        const string source = """
            namespace TestConsumer
            {
                public interface IMyGenericInterface<T> { }
                public abstract class MyBase<T> : IMyGenericInterface<T> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Where(static d => d.Id == "BPMIG202").Should().BeEmpty();
    }

    [Xunit.Fact]
    public async Task When_abstract_generic_record_implements_IQuery_then_reports_bpmig202()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract record PagedQuery<T> : Blueprint.Model.IQuery<T>;
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        diagnostics.Should().ContainSingle(static d => d.Id == "BPMIG202");
    }

    [Xunit.Fact]
    public async Task Diagnostic_location_targets_interface_in_base_list()
    {
        const string source = """
            namespace Blueprint.Model
            {
                public interface IOperation { }
                public interface IQuery<T> : IOperation { }
            }

            namespace TestConsumer
            {
                public abstract class OrderedQuery<T> : Blueprint.Model.IQuery<T> { }
            }
            """;

        var diagnostics = await GetDiagnosticsAsync(source);

        var diagnostic = diagnostics.Single(static d => d.Id == "BPMIG202");
        var lineSpan = diagnostic.Location.GetMappedLineSpan();
        lineSpan.StartLinePosition.Line.Should().Be(8, "diagnostic should point at the base type on line 8 (0-based)");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName: "AbstractBaseMigrationTests",
            syntaxTrees: [syntaxTree],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Analyzers.AbstractBaseOperationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
