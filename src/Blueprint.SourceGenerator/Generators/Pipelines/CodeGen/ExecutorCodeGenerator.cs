using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;

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

        var classBuilder = codeBuilder.AddClass($"{model.AppTypeName}Executor")
            .MakePublicClass()
            .SetBaseClass("Blueprint.BlueprintAppExecutor");

        classBuilder.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .AddParameter($"{model.AppNamespace}.{model.AppTypeName}", "app")
            .WithBaseCallArguments("app")
            .WithBody(
                writer =>
                {
                    foreach (var handler in model.Handlers)
                    {
                        var pipelineType = $"Blueprint.Generated.{handler.PipelineClassName}";
                        writer.AppendLine($"this.Register(typeof({handler.OperationTypeFullName}), new {pipelineType}(loggerFactory));");
                    }
                });

        return codeBuilder.Build();
    }
}
