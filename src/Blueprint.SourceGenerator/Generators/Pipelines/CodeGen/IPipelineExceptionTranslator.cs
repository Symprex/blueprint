using Blueprint.SourceGenerator.CodeWriter;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Emits the executor-shell catch blocks that translate exceptions into operation results.
/// Custom implementations can replace the default shell without widening the public result contract.
/// </summary>
internal interface IPipelineExceptionTranslator
{
    /// <summary>
    /// Writes the exception-handling shell for a generated pipeline execute method.
    /// </summary>
    /// <param name="writer">The writer receiving the generated catch blocks.</param>
    /// <param name="isHandlerAsync">Whether the generated execute method returns directly or must wrap synchronous results.</param>
    void WriteExceptionHandling(ICodeWriter writer, bool isHandlerAsync);
}

/// <summary>
/// Preserves Blueprint's current validation, <c>Blueprint.ApiException</c>, and generic unhandled-exception fallback behavior.
/// </summary>
internal sealed class DefaultPipelineExceptionTranslator : IPipelineExceptionTranslator
{
    /// <summary>
    /// Gets the shared default translator instance.
    /// </summary>
    public static DefaultPipelineExceptionTranslator Instance { get; } = new();

    private DefaultPipelineExceptionTranslator()
    {
    }

    /// <inheritdoc />
    public void WriteExceptionHandling(ICodeWriter writer, bool isHandlerAsync)
    {
        writer.Catch(
            "Blueprint.Validation.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return(PipelineCodeGenerator.WrapOperationResult("new Blueprint.Runtime.Exec.ValidationFailedResult(e.ValidationResults)", isHandlerAsync));
            });

        writer.Catch(
            "System.ComponentModel.DataAnnotations.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return(PipelineCodeGenerator.WrapOperationResult("Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedResult(e)", isHandlerAsync));
            });

        writer.Catch(
            "System.Exception e",
            catchWriter =>
            {
                catchWriter.If(
                    "e is Blueprint.ApiException apiException && apiException.HttpStatus < 500",
                    ifWriter =>
                    {
                        ifWriter.AppendLine("_NonException(_logger, e.Message, e);");
                        ifWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                    });

                catchWriter.Else(
                    elseWriter =>
                    {
                        elseWriter.AppendLine("Blueprint.Diagnostics.BlueprintActivitySource.RecordException(context.Activity, e, false);");
                        elseWriter.AppendLine("_UnhandledException(_logger, e.Message, e);");
                        elseWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"ERROR\");");
                        elseWriter.AppendLine("context.Activity?.SetTag(\"otel.status_description\", e.Message);");
                    });

                catchWriter.NewLine();
                catchWriter.Return(PipelineCodeGenerator.WrapOperationResult("new Blueprint.Runtime.Exec.UnhandledExceptionResult(e)", isHandlerAsync));
            });
    }
}
