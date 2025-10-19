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
        writer.AppendLine($"namespace {model.AppNamespace}");
        writer.AppendLine("{");
        writer.Indent();

        // Class declaration
        writer.AppendLine($"public partial class {model.AppTypeName}Executor : Blueprint.BlueprintAppExecutor");
        writer.AppendLine("{");
        writer.Indent();

        // Constructor
        writer.AppendLine($"public {model.AppTypeName}Executor(");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,");
        writer.AppendLine($"{model.AppNamespace}.{model.AppTypeName} app)");
        writer.Unindent();
        writer.Indent();
        writer.AppendLine(": base(app)");
        writer.Unindent();
        writer.AppendLine("{");
        writer.Indent();

        // Register all pipelines
        foreach (var handler in model.Handlers)
        {
            var pipelineType = $"Blueprint.Generated.{handler.PipelineClassName}";
            writer.AppendLine($"this.Register(typeof({handler.OperationTypeFullName}), new {pipelineType}(loggerFactory));");
        }

        writer.Unindent();
        writer.AppendLine("}");

        writer.Unindent();
        writer.AppendLine("}");

        writer.Unindent();
        writer.AppendLine("}");

        return writer.ToString();
    }
}
