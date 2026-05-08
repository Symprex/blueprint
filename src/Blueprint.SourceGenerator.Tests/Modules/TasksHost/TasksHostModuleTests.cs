using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.TasksHost;

/// <summary>
/// Tests that <see cref="TasksHostModule"/> correctly identifies background task operations
/// and records host exposure metadata in descriptor plans.
/// </summary>
public class TasksHostModuleTests
{
    [Fact]
    public void When_operation_implements_IBackgroundTask_then_descriptor_has_tasks_host_metadata()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class SendEmailTask : ICommand<string>, IBackgroundTask
            {
                public static Task<string> HandleAsync(SendEmailTask operation)
                {
                    return Task.FromResult("sent");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TasksHostModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var executionPlan = config.BuildExecutionPlan();
        executionPlan.Diagnostics.Should().BeEmpty();

        var operationType = compilation.GetTypeByMetadataName("TestNamespace.SendEmailTask")!;
        var operationModel = OperationModel.Create(operationType);
        var descriptorPlan = new DescriptorPlan(operationModel);
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlan.Value!.DescriptorContributors)
        {
            contributor.ContributeToDescriptor(new DescriptorContributorContext(operationModel, descriptorPlan, operationLookup));
        }

        var hostExposures = descriptorPlan.GetMetadata<HostExposureRegistration>().ToArray();
        hostExposures.Should().Contain(static h => h.HostName == "tasks");
    }

    [Fact]
    public void When_operation_does_not_implement_IBackgroundTask_then_no_tasks_host_metadata()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TasksHostModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var executionPlan = config.BuildExecutionPlan();
        executionPlan.Diagnostics.Should().BeEmpty();

        var operationType = compilation.GetTypeByMetadataName("TestNamespace.GetOrderQuery")!;
        var operationModel = OperationModel.Create(operationType);
        var descriptorPlan = new DescriptorPlan(operationModel);
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlan.Value!.DescriptorContributors)
        {
            contributor.ContributeToDescriptor(new DescriptorContributorContext(operationModel, descriptorPlan, operationLookup));
        }

        var hostExposures = descriptorPlan.GetMetadata<HostExposureRegistration>().ToArray();
        hostExposures.Should().NotContain(static h => h.HostName == "tasks");
    }

    [Fact]
    public void When_tasks_host_module_then_host_names_includes_tasks()
    {
        var module = new TasksHostModule();
        module.HostNames.Should().ContainSingle().Which.Should().Be("tasks");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Tasks.IBackgroundTask).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "TasksHostModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
