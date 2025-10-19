using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using CodeGenHelpers;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Generates the BlueprintAppExecutor code with all pipeline registrations.
/// </summary>
internal static class ExecutorCodeGenerator
{
    /// <summary>
    /// Generates the complete executor class with all pipeline registrations.
    /// </summary>
    public static string GenerateExecutor(PipelineCompilationModel model)
    {
        var codeBuilder = CodeBuilder.Create(model.AppNamespace);

        var executorClass = codeBuilder.AddClass($"{model.AppTypeName}Executor")
            .MakePartial()
            .WithAccessModifier(Accessibility.Public)
            .SetBaseClass("Blueprint.BlueprintAppExecutor");

        // Add constructor with all registrations
        executorClass.AddConstructor()
            .WithAccessModifier(Accessibility.Public)
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .AddParameter($"{model.AppNamespace}.{model.AppTypeName}", "app")
            .WithBaseCall(new Dictionary<string, string> { { "app", "app" } })
            .WithBody(b =>
            {
                foreach (var handler in model.Handlers)
                {
                    var pipelineType = $"Blueprint.Generated.{handler.PipelineClassName}";
                    b.AppendLine($"this.Register(typeof({handler.OperationTypeFullName}), new {pipelineType}(loggerFactory));");
                }
            });

        return codeBuilder.Build();
    }
}
