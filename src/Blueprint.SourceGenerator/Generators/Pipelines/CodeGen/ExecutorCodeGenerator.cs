using Blueprint.SourceGenerator.CodeWriter;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Generates the BlueprintAppExecutor code with all pipeline registrations.
/// </summary>
internal static class ExecutorCodeGenerator
{
    /// <summary>
    /// Generates the complete executor class with all pipeline registrations.
    /// </summary>
    /// <param name="model">The compilation model containing the app type and generated pipelines to register.</param>
    /// <returns>The generated executor source for the Blueprint app.</returns>
    public static string GenerateExecutor(PipelineCompilationModel model)
    {
        var codeBuilder = CodeBuilder.Create(model.AppNamespace);

        var classBuilder = codeBuilder.AddClass($"{model.AppTypeName}Executor")
            .MakePublicClass()
            .Sealed()
            .SetBaseClass("Blueprint.BlueprintAppExecutor");

        classBuilder.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .AddParameter($"{model.AppNamespace}.{model.AppTypeName}", "app")
            .AddParameter("System.IServiceProvider", "serviceProvider")
            .WithBaseCallArguments("app")
            .WithBody(
                writer =>
                {
                    foreach (var handler in model.Handlers)
                    {
                        var pipelineType = $"Blueprint.Generated.{handler.PipelineClassName}";
                        writer.AppendLine($"this.Register(typeof({handler.OperationTypeFullName}), new {pipelineType}(loggerFactory, serviceProvider));");
                    }

                    // Freeze the pipeline dictionary after all registrations for optimal dispatch performance
                    writer.AppendLine("this.Freeze();");
                });

        return codeBuilder.Build();
    }
}
