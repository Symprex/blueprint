using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;

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
        var writer = new SourceWriter();

        // Namespace
        writer.Block($"namespace {model.AppNamespace}");

        // Class declaration
        writer.Block($"public partial class {model.AppTypeName}Executor : Blueprint.BlueprintAppExecutor");

        // Constructor - need special formatting for multi-line signature
        writer.Indent().Append($"public {model.AppTypeName}Executor(").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Append("Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Append($"{model.AppNamespace}.{model.AppTypeName} app)").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Append(": base(app)").Append(SourceWriter.NewLine);
        writer.Block(string.Empty); // Start constructor body

        // Register all pipelines
        foreach (var handler in model.Handlers)
        {
            var pipelineType = $"Blueprint.Generated.{handler.PipelineClassName}";
            writer.WriteLine($"this.Register(typeof({handler.OperationTypeFullName}), new {pipelineType}(loggerFactory));");
        }

        writer.FinishBlock(); // End constructor

        writer.FinishBlock(); // End class

        writer.FinishBlock(); // End namespace

        return writer.ToString();
    }
}
