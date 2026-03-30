using Blueprint.SourceGenerator.CodeWriter;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Emits one catch block inside the ordered executor-shell exception translation policy.
/// Translators declare the exception family they own so the policy can keep catch ordering deterministic and reject
/// accidental overlap before generation continues.
/// </summary>
internal interface IPipelineExceptionTranslator
{
    /// <summary>
    /// Gets the stable identifier used in diagnostics and deterministic ordering.
    /// </summary>
    string TranslatorId { get; }

    /// <summary>
    /// Gets the primary exception type name owned by this translator.
    /// This value is used for deterministic ordering and for the top-level type called out in diagnostics.
    /// </summary>
    string HandledExceptionTypeName { get; }

    /// <summary>
    /// Gets the exception family names claimed by this translator for conflict detection.
    /// Most translators only need their direct catch type, while more specific translators can also claim broader family
    /// names when overlap with another non-fallback translator would be ambiguous.
    /// </summary>
    IReadOnlyList<string> OwnedExceptionTypeNames { get; }

    /// <summary>
    /// Gets the relative ordering used before the policy writes catch blocks.
    /// Lower values run earlier inside the same fallback category.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Gets a value indicating whether this translator owns the final catch-all path.
    /// Fallback translators are always emitted after specific translators so catch ordering stays legal and explicit.
    /// </summary>
    bool IsFallback { get; }

    /// <summary>
    /// Writes the catch block owned by this translator for a generated pipeline execute method.
    /// </summary>
    /// <param name="writer">The writer receiving the generated catch blocks.</param>
    /// <param name="isHandlerAsync">Whether the generated execute method returns directly or must wrap synchronous results.</param>
    void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync);
}

/// <summary>
/// Preserves the legacy Blueprint validation-exception compatibility path as a specific translator.
/// </summary>
internal sealed class LegacyValidationExceptionTranslator : IPipelineExceptionTranslator
{
    /// <summary>
    /// Gets the shared translator instance.
    /// </summary>
    public static LegacyValidationExceptionTranslator Instance { get; } = new();

    private LegacyValidationExceptionTranslator()
    {
    }

    /// <inheritdoc />
    public string TranslatorId => "legacy-validation-exception";

    /// <inheritdoc />
    public string HandledExceptionTypeName => "Blueprint.Validation.ValidationException";

    /// <inheritdoc />
    public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["Blueprint.Validation.ValidationException"];

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool IsFallback => false;

    /// <inheritdoc />
    public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
    {
        writer.Catch(
            "Blueprint.Validation.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return(PipelineCodeGenerator.WrapOperationResult("new Blueprint.Runtime.Exec.ValidationFailedResult(e.ValidationResults)", isHandlerAsync));
            });
    }
}

/// <summary>
/// Preserves the framework <c>System.ComponentModel.DataAnnotations.ValidationException</c> compatibility path as a
/// specific translator so it stays ordered ahead of the catch-all fallback.
/// </summary>
internal sealed class DataAnnotationsValidationExceptionTranslator : IPipelineExceptionTranslator
{
    /// <summary>
    /// Gets the shared translator instance.
    /// </summary>
    public static DataAnnotationsValidationExceptionTranslator Instance { get; } = new();

    private DataAnnotationsValidationExceptionTranslator()
    {
    }

    /// <inheritdoc />
    public string TranslatorId => "data-annotations-validation-exception";

    /// <inheritdoc />
    public string HandledExceptionTypeName => "System.ComponentModel.DataAnnotations.ValidationException";

    /// <inheritdoc />
    public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["System.ComponentModel.DataAnnotations.ValidationException"];

    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />
    public bool IsFallback => false;

    /// <inheritdoc />
    public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
    {
        writer.Catch(
            "System.ComponentModel.DataAnnotations.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return(PipelineCodeGenerator.WrapOperationResult("Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedResult(e)", isHandlerAsync));
            });
    }
}

/// <summary>
/// Preserves Blueprint's current <c>Blueprint.ApiException</c> logging path plus the generic unhandled-exception
/// fallback behavior.
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
    public string TranslatorId => "default-unhandled-exception";

    /// <inheritdoc />
    public string HandledExceptionTypeName => "System.Exception";

    /// <inheritdoc />
    public IReadOnlyList<string> OwnedExceptionTypeNames { get; } = ["System.Exception"];

    /// <inheritdoc />
    public int Order => int.MaxValue;

    /// <inheritdoc />
    public bool IsFallback => true;

    /// <inheritdoc />
    public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
    {
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
