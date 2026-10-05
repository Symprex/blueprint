using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Rebus;

/// <summary>
/// Tests for the <see cref="RebusTransactionModule"/> proving that the Rebus transaction scope
/// is correctly contributed to the generated pipeline and respects the NoRebusTransaction opt-out.
/// </summary>
public class RebusTransactionModuleTests
{
    [Fact]
    public void When_command_operation_then_plan_contains_rebus_validation_and_post_execution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class CreateOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(CreateOrderCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.CreateOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var contributions = result.Value!.Contributions;

        // Opened in Validation, not Setup: legacy opened the scope after validation, straight before the handler (legacy
        // AzureAdOauthReturnCommandExecutorPipeline.cs:91-117 at Signature365 8b40ecc961).
        contributions.Should().ContainInOrder(
            new PipelineContribution(PipelineStage.Validation, nameof(RebusTransactionModule)),
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)),
            new PipelineContribution(PipelineStage.PostExecution, nameof(RebusTransactionModule)));
        contributions.Should().NotContain(
            new PipelineContribution(PipelineStage.Setup, nameof(RebusTransactionModule)),
            "the scope opens after validation, not before tenant resolution and authorisation");
    }

    [Fact]
    public void When_query_operation_then_plan_contains_rebus_validation_and_post_execution()
    {
        // R138: legacy's StartRebusTransactionScopeMiddleware matched any operation without [NoRebusTransaction]
        // (Signature365 8b40ecc961, BlueprintRebus/StartRebusTransactionScopeMiddleware.cs:25-30), queries included:
        // Portal's Microsoft365AddOnConnectQueryExecutorPipeline.cs:114-131 opens, enlists and completes the scope.
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrderQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().ContainInOrder(
            new PipelineContribution(PipelineStage.Validation, nameof(RebusTransactionModule)),
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)),
            new PipelineContribution(PipelineStage.PostExecution, nameof(RebusTransactionModule)));
    }

    [Fact]
    public void When_query_saves_a_unit_of_work_then_its_scope_opens_and_is_enlisted_unless_nested()
    {
        // R138: a query opens the scope and enlists as a command does (legacy Microsoft365AddOnConnectQueryExecutorPipeline.cs
        // :114-119), except in a nested execution (a resource event's self query), which legacy ran without either
        // (StartRebusTransactionScopeMiddleware and UseMartenAsOutboxMiddleware: SupportsNestedExecution => false).
        var lines = GeneratePipelineLines(
            """
            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation) => Task.FromResult("order");
            }
            """,
            includeBlueprintRebus: true);

        var scopeLine = lines.FindIndex(static l => l.StartsWith("using var rebusTransactionScope", StringComparison.Ordinal));
        var handlerLine = lines.FindIndex(static l => l.Contains("GetOrderQuery.HandleAsync(operation)", StringComparison.Ordinal));

        scopeLine.Should().BeGreaterThanOrEqualTo(0);
        scopeLine.Should().BeLessThan(handlerLine);
        lines.Skip(scopeLine).Take(4).Should().Equal(
            "using var rebusTransactionScope = this._hasRebus && !context.IsNested ? new global::Rebus.Transport.RebusTransactionScope() : null;",
            "if (rebusTransactionScope != null && this.HasUnitOfWork)",
            "{",
            "await global::Blueprint.Rebus.RebusTransactionEnlistment.EnlistAsync(rebusTransactionScope.TransactionContext, context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
        lines.Should().Contain("if (rebusTransactionScope != null) await rebusTransactionScope.CompleteAsync().ConfigureAwait(false);");
    }

    [Fact]
    public void When_query_does_not_save_a_unit_of_work_then_its_scope_still_opens_but_is_not_enlisted()
    {
        // Legacy opened the scope for a [DoNotSaveChanges] query as for any other (its StartRebusTransactionScopeMiddleware
        // looked only for [NoRebusTransaction]) and enlisted nothing (UseMartenAsOutboxMiddleware.cs:26-31).
        var lines = GeneratePipelineLines(
            """
            [Blueprint.Persistence.DoNotSaveChanges]
            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation) => Task.FromResult("order");
            }
            """,
            includeBlueprintRebus: true);

        lines.Should().Contain("using var rebusTransactionScope = this._hasRebus && !context.IsNested ? new global::Rebus.Transport.RebusTransactionScope() : null;");
        lines.Should().NotContain(static l => l.Contains("RebusTransactionEnlistment", StringComparison.Ordinal));
        lines.Should().NotContain(static l => l.Contains("SaveUnitOfWorkAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void When_query_has_NoRebusTransaction_then_it_opens_no_scope()
    {
        var lines = GeneratePipelineLines(
            """
            [Blueprint.Rebus.NoRebusTransaction]
            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation) => Task.FromResult("order");
            }
            """,
            includeBlueprintRebus: true);

        lines.Should().NotContain(static l => l.Contains("rebusTransactionScope", StringComparison.Ordinal));
    }

    [Fact]
    public void When_NoRebusTransaction_attribute_then_plan_excludes_rebus_transaction()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Rebus;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [NoRebusTransaction]
            public partial class FireAndForgetCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(FireAndForgetCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.FireAndForgetCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(RebusTransactionModule),
            "NoRebusTransaction should exclude the module");
    }

    [Fact]
    public void When_background_task_then_plan_contains_rebus_validation_and_post_execution()
    {
        // Legacy gave background tasks the same Rebus transaction scope as commands, so a task that publishes has its
        // messages held until it succeeds.
        var compilation = CreateCompilation(
            """
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class SendReminderTask : IBackgroundTask
            {
                public static Task HandleAsync(SendReminderTask operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.SendReminderTask", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        // As for a command: legacy's task pipelines validated, then opened the scope (legacy
        // BillingSoftDeleteBackgroundTaskExecutorPipeline.cs:48-51 at Signature365 8b40ecc961).
        result.Value!.Contributions.Should().ContainInOrder(
            new PipelineContribution(PipelineStage.Validation, nameof(RebusTransactionModule)),
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)),
            new PipelineContribution(PipelineStage.PostExecution, nameof(RebusTransactionModule)));
        result.Value!.Contributions.Should().NotContain(
            new PipelineContribution(PipelineStage.Setup, nameof(RebusTransactionModule)),
            "the scope opens after validation, not before tenant resolution and authorisation");
    }

    [Fact]
    public void When_background_task_has_NoRebusTransaction_attribute_then_plan_excludes_rebus_transaction()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Rebus;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [NoRebusTransaction]
            public partial class FireAndForgetTask : IBackgroundTask
            {
                public static Task HandleAsync(FireAndForgetTask operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.FireAndForgetTask", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(RebusTransactionModule),
            "NoRebusTransaction should exclude a background task from the scope exactly as it does a command");
    }

    [Fact]
    public async Task When_background_task_composed_with_pipeline_generator_then_snapshot_includes_rebus_scope()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class SendReminderTask : IBackgroundTask
                {
                    public static Task HandleAsync(SendReminderTask operation)
                    {
                        return Task.CompletedTask;
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<RebusCompanionPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"],
            [
                MetadataReference.CreateFromFile(typeof(global::Rebus.Transport.RebusTransactionScope).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Blueprint.Rebus.NoRebusTransactionAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Blueprint.Marten.MartenScopeAccessor).Assembly.Location),
            ]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(RebusCompanionPipelineSourceGenerator));

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    [Fact]
    public void When_composed_with_core_modules_then_rebus_complete_is_after_uow_and_outbox()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class PlaceOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(PlaceOrderCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new PropertyPopulationModule(),
            new HandlerExecutionModule(),
            new UnitOfWorkModule(),
            new MessageOutboxModule(),
            new AuditingModule(),
            new BackgroundTaskFlushModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.PlaceOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var postExecutionContributions = result.Value!.Contributions
            .Where(static c => c.Stage == PipelineStage.PostExecution)
            .Select(static c => c.ContributorId)
            .ToList();

        // Rebus is a Consumer module (Category=1), core modules are Category=0.
        // The ordering invariant is: core PostExecution steps complete BEFORE
        // RebusTransactionScope.CompleteAsync(), ensuring data is persisted and
        // outbox is committed before Rebus messages are dispatched.
        postExecutionContributions.Should().ContainInOrder(
            nameof(UnitOfWorkModule),
            nameof(MessageOutboxModule),
            nameof(AuditingModule),
            nameof(BackgroundTaskFlushModule),
            nameof(RebusTransactionModule));
    }

    [Fact]
    public async Task When_composed_with_pipeline_generator_then_snapshot_includes_rebus_scope()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class PlaceOrderCommand : ICommand<string>
                {
                    public static Task<string> HandleAsync(PlaceOrderCommand operation)
                    {
                        return Task.FromResult("ordered");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<RebusCompanionPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"],
            [
                MetadataReference.CreateFromFile(typeof(global::Rebus.Transport.RebusTransactionScope).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Blueprint.Rebus.NoRebusTransactionAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Blueprint.Marten.MartenScopeAccessor).Assembly.Location),
            ]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(RebusCompanionPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated with Rebus module");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    [Fact]
    public void When_command_saves_a_unit_of_work_then_the_scope_is_enlisted_after_tenant_resolution_authorisation_and_validation_and_before_the_handler()
    {
        // Legacy enlisted the outbox from Execution-stage middleware added before the handler
        // (legacy BlueprintRebusPipelineExtensions.cs:41-42, at priority -1 by PipelineBuilder.cs:26-31, ahead of the
        // handler's priority 0 at :19), so after SetTenant (legacy AzureAdOauthReturnCommandExecutorPipeline.cs:61-66),
        // authorisation (:68-89) and validation (:91-114), and straight before the handler (:119-121, then :123-129).
        // An enlistment resolving a tenant-scoped session any earlier would get an untenanted one.
        var lines = GeneratePipelineLines(
            """
            [Blueprint.Model.Attributes.RootLink("/orders")]
            public partial class PlaceOrderCommand : ICommand<string>
            {
                [System.ComponentModel.DataAnnotations.Required]
                public string Reference { get; set; }

                public static Task<string> HandleAsync(PlaceOrderCommand operation) => Task.FromResult("ordered");
            }
            """,
            includeBlueprintRebus: true);

        var scopeLine = lines.FindIndex(static l => l.StartsWith("using var rebusTransactionScope", StringComparison.Ordinal));
        var tenantLine = lines.FindIndex(static l => l.StartsWith("ResolveTenant(", StringComparison.Ordinal));
        var authenticationLine = lines.FindIndex(static l => l.Contains("CheckHttpAuthenticated(", StringComparison.Ordinal));
        var authorisationLine = lines.FindIndex(static l => l.Contains("RunPreFilteredAuthorisersAsync(", StringComparison.Ordinal));
        var lastValidationLine = lines.FindLastIndex(static l => l.Contains("CreateValidationFailure(", StringComparison.Ordinal));
        var enlistLine = lines.FindIndex(static l => l.Contains("RebusTransactionEnlistment.EnlistAsync(", StringComparison.Ordinal));
        var handlerLine = lines.FindIndex(static l => l.Contains("PlaceOrderCommand.HandleAsync(operation)", StringComparison.Ordinal));

        new[] { scopeLine, tenantLine, authenticationLine, authorisationLine, lastValidationLine, enlistLine, handlerLine }
            .Should().OnlyContain(static index => index >= 0, "every step the ordering is about must be in the pipeline");

        enlistLine.Should().BeGreaterThan(tenantLine, "the enlistment must see the resolved tenant");
        enlistLine.Should().BeGreaterThan(authenticationLine, "legacy enlisted only an authenticated operation");
        enlistLine.Should().BeGreaterThan(authorisationLine, "legacy enlisted only an authorised operation");
        enlistLine.Should().BeGreaterThan(lastValidationLine, "legacy enlisted only a valid operation");
        enlistLine.Should().BeLessThan(handlerLine, "the outbox must be enlisted before the handler sends anything");

        // Legacy opened the scope at the same point, straight before it enlisted (StartRebusTransactionScopeMiddleware,
        // legacy AzureAdOauthReturnCommandExecutorPipeline.cs:116-117, then :119-121), so a message sent while the tenant
        // was set, the operation authorised or validated (:61-114) went outside it.
        scopeLine.Should().BeGreaterThan(tenantLine, "legacy set the tenant before it opened the scope");
        scopeLine.Should().BeGreaterThan(authorisationLine, "legacy authorised before it opened the scope");
        scopeLine.Should().BeGreaterThan(lastValidationLine, "legacy validated before it opened the scope");

        lines.Skip(scopeLine).Take(4).Should().Equal(
            "using var rebusTransactionScope = this._hasRebus ? new global::Rebus.Transport.RebusTransactionScope() : null;",
            "if (rebusTransactionScope != null && this.HasUnitOfWork)",
            "{",
            "await global::Blueprint.Rebus.RebusTransactionEnlistment.EnlistAsync(rebusTransactionScope.TransactionContext, context.ServiceProvider, context.OperationCancelled).ConfigureAwait(false);");
    }

    [Fact]
    public void When_command_does_not_save_a_unit_of_work_then_the_scope_still_opens_after_validation_and_before_the_handler()
    {
        // Legacy opened the scope after validation whether or not it went on to enlist an outbox
        // (legacy AzureAdOauthReturnCommandExecutorPipeline.cs:91-117).
        var lines = GeneratePipelineLines(
            """
            [Blueprint.Persistence.DoNotSaveChanges]
            public partial class ValidatedFireAndForgetCommand : ICommand<string>
            {
                [System.ComponentModel.DataAnnotations.Required]
                public string Reference { get; set; }

                public static Task<string> HandleAsync(ValidatedFireAndForgetCommand operation) => Task.FromResult("sent");
            }
            """,
            includeBlueprintRebus: true);

        var scopeLine = lines.FindIndex(static l => l.StartsWith("using var rebusTransactionScope", StringComparison.Ordinal));
        var lastValidationLine = lines.FindLastIndex(static l => l.Contains("CreateValidationFailure(", StringComparison.Ordinal));
        var handlerLine = lines.FindIndex(static l => l.Contains("ValidatedFireAndForgetCommand.HandleAsync(operation)", StringComparison.Ordinal));

        new[] { scopeLine, lastValidationLine, handlerLine }.Should().OnlyContain(static index => index >= 0);

        scopeLine.Should().BeGreaterThan(lastValidationLine);
        scopeLine.Should().BeLessThan(handlerLine);
    }

    [Fact]
    public void When_command_does_not_save_a_unit_of_work_then_the_scope_is_not_enlisted()
    {
        var lines = GeneratePipelineLines(
            """
            [Blueprint.Persistence.DoNotSaveChanges]
            public partial class FireAndForgetCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(FireAndForgetCommand operation) => Task.FromResult("sent");
            }
            """,
            includeBlueprintRebus: true);

        lines.Should().Contain(static l => l.Contains("using var rebusTransactionScope", StringComparison.Ordinal));
        lines.Should().NotContain(static l => l.Contains("RebusTransactionEnlistment", StringComparison.Ordinal),
            "an outbox enlisted where nothing is saved would never be written");
    }

    [Fact]
    public void When_Rebus_is_referenced_without_Blueprint_Rebus_then_the_scope_opens_without_enlistment_and_compiles()
    {
        var lines = GeneratePipelineLines(
            """
            public partial class PlaceOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(PlaceOrderCommand operation) => Task.FromResult("ordered");
            }
            """,
            includeBlueprintRebus: false);

        lines.Should().Contain(static l => l.Contains("using var rebusTransactionScope", StringComparison.Ordinal));
        lines.Should().NotContain(static l => l.Contains("RebusTransactionEnlistment", StringComparison.Ordinal));
    }

    [Fact]
    public void When_rebus_not_referenced_then_module_does_not_contribute()
    {
        // Compilation WITHOUT Rebus assembly references — only Blueprint.Runtime.Model
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class SimpleCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(SimpleCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(static assembly => !assembly.GetName().Name!.StartsWith("Rebus", StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "NoRebusTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var config = new BlueprintSourceGenerationConfig(
        [
            new RebusTransactionModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.SimpleCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(RebusTransactionModule),
            "RebusTransactionModule should auto-detect and skip when Rebus is not referenced");
    }

    private static List<string> GeneratePipelineLines(string operationSource, bool includeBlueprintRebus)
    {
        var source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

            """ + operationSource + "\n}\n";

        // Built by hand rather than through TestHelpers, whose default references include every loaded Blueprint
        // assembly — Blueprint.Rebus among them — so a compilation without it could not otherwise be tested.
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(static assembly =>
            {
                var name = assembly.GetName().Name!;
                return !name.StartsWith("Rebus", StringComparison.OrdinalIgnoreCase)
                       && !name.Equals("Blueprint.Rebus", StringComparison.OrdinalIgnoreCase);
            })
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        // The same explicit references TestHelpers adds, for assemblies that may not have been loaded yet.
        foreach (var type in new[]
                 {
                     typeof(HttpRequestMessage),
                     typeof(System.Net.Http.Headers.AuthenticationHeaderValue),
                     typeof(Blueprint.Model.ICommand),
                     typeof(Blueprint.OperationResult),
                     typeof(Blueprint.Runtime.Exec.ApiOperationContext),
                     typeof(System.ComponentModel.DataAnnotations.ValidationResult),
                     typeof(Microsoft.Extensions.Logging.ILogger),
                     typeof(Microsoft.Extensions.Logging.LoggerMessage),
                     typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger),
                     typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions),
                     typeof(Microsoft.Extensions.Options.IOptions<>),
                     typeof(System.Diagnostics.Activity),
                     typeof(System.Text.Json.Serialization.JsonSerializerContext),
                     typeof(Microsoft.Extensions.Primitives.StringValues),
                     typeof(Microsoft.AspNetCore.Http.IQueryCollection),
                     typeof(Microsoft.AspNetCore.Http.HttpContext),
                     typeof(Blueprint.HttpClient.BlueprintHttpClient<>),
                     typeof(global::Rebus.Transport.RebusTransactionScope),
                     typeof(Blueprint.Marten.MartenScopeAccessor),
                 })
        {
            references.Add(MetadataReference.CreateFromFile(type.Assembly.Location));
        }

        if (includeBlueprintRebus)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Rebus.NoRebusTransactionAttribute).Assembly.Location));
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "RebusEnlistmentTests",
            [CSharpSyntaxTree.ParseText(source, parseOptions, "Input0.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            [new OperationDescriptorSourceGenerator().AsSourceGenerator(), new RebusCompanionPipelineSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        output.Emit(new MemoryStream()).Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated pipeline must compile against exactly these references");

        return output.SyntaxTrees
            .Single(static tree => tree.FilePath.EndsWith("ExecutorPipeline.g.cs", StringComparison.Ordinal))
            .ToString()
            .Split('\n')
            .Select(static l => l.Trim())
            .ToList();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Rebus.NoRebusTransactionAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::Rebus.Transport.RebusTransactionScope).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "RebusTransactionModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A derived pipeline source generator that uses the default config (which now auto-detects Rebus).
/// The Rebus module activates automatically when <c>Rebus.Transport.RebusTransactionScope</c>
/// is found in the compilation, so no companion module composition is needed.
/// </summary>
internal sealed class RebusCompanionPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
{
    public RebusCompanionPipelineSourceGenerator()
        : base(CreateDefaultConfig())
    {
    }
}
