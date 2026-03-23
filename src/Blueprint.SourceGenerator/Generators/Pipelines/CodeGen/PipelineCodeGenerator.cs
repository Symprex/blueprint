using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Generates the executor pipeline code for a single operation.
/// </summary>
internal static class PipelineCodeGenerator
{
    /// <summary>
    /// Generates the complete pipeline class for an operation handler.
    /// </summary>
    /// <param name="handler">The discovered handler model that drives generated type and method names.</param>
    /// <param name="plan">The contributor-built plan that determines the rendered pipeline steps.</param>
    /// <returns>The generated pipeline source for the supplied handler.</returns>
    public static string GeneratePipeline(OperationHandlerModel handler, PipelinePlan plan)
    {
        var codeBuilder = CodeBuilder.Create("Blueprint.Generated");

        var pipelineClass = codeBuilder.AddClass(handler.PipelineClassName)
            .MakePublicClass()
            .AddInterface("Blueprint.IOperationExecutorPipeline");

        AddLoggingFields(pipelineClass);
        AddLoggerField(pipelineClass);
        AddConstructor(pipelineClass, handler);
        AddExecuteAsyncMethod(pipelineClass, handler, plan);

        return codeBuilder.Build();
    }

    private static void AddLoggingFields(ClassBuilder pipelineClass)
    {
        AddLoggingField(
            pipelineClass,
            "_ApiOperationExecuting",
            "Debug",
            1,
            "ApiOperationExecuting",
            "Executing API operation {OperationType} with handler");
        AddLoggingField(
            pipelineClass,
            "_NonException",
            "Information",
            4,
            "Non500Exception",
            "A non-5xx ApiException has occurred with message {ExceptionMessage}");
        AddLoggingField(
            pipelineClass,
            "_UnhandledException",
            "Error",
            5,
            "UnhandledException",
            "An unhandled exception has occurred with message {ExceptionMessage}");
    }

    private static void AddLoggingField(
        ClassBuilder pipelineClass,
        string fieldName,
        string logLevel,
        int eventId,
        string eventName,
        string message)
    {
        pipelineClass.AddProperty(fieldName)
            .MakePrivateProperty()
            .MakeStatic()
            .SetType("System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception>")
            .WithReadonlyValue(
                $"Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(Microsoft.Extensions.Logging.LogLevel.{logLevel}, new Microsoft.Extensions.Logging.EventId({eventId}, \"{eventName}\"), \"{message}\")");
    }

    private static void AddLoggerField(ClassBuilder pipelineClass)
    {
        pipelineClass.AddProperty("_logger")
            .MakePrivateProperty()
            .SetType("Microsoft.Extensions.Logging.ILogger")
            .WithReadonlyValue();
    }

    private static void AddConstructor(ClassBuilder pipelineClass, OperationHandlerModel handler)
    {
        pipelineClass.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .WithBody(writer => writer.Assign("_logger", $"loggerFactory.CreateLogger(\"{handler.PipelineClassName}\")"));
    }

    private static void AddExecuteAsyncMethod(ClassBuilder pipelineClass, OperationHandlerModel handler, PipelinePlan plan)
    {
        var executeMethod = pipelineClass.AddMethod("ExecuteAsync")
            .MakePublicMethod()
            .WithReturnType("System.Threading.Tasks.Task<Blueprint.OperationResult>")
            .AddParameter("Blueprint.Runtime.Exec.ApiOperationContext", "context");

        if (handler.IsHandlerAsync)
        {
            executeMethod.MakeAsync();
        }

        executeMethod.WithBody(
            writer =>
            {
                writer.AppendLine($"using var activity = Blueprint.Diagnostics.BlueprintActivitySource.ActivitySource.StartActivity(\"{handler.OperationTypeName}Pipeline\", System.Diagnostics.ActivityKind.Internal);");
                writer.DeclareVar("operation", $"({handler.OperationTypeFullName})context.Operation");
                writer.NewLine();

                writer.Try(
                    tryWriter =>
                    {
                        tryWriter.AppendLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
                        tryWriter.NewLine();
                        WritePipelineSteps(tryWriter, handler, plan);
                    });

                WriteExceptionHandling(writer, handler.IsHandlerAsync);
            });
    }

    private static void WritePipelineSteps(ICodeWriter writer, OperationHandlerModel handler, PipelinePlan plan)
    {
        foreach (var contribution in plan.Contributions)
        {
            foreach (var renderStep in plan.GetRenderSteps(contribution))
            {
                renderStep.Render(new PipelineRenderContext(writer, handler, plan));
            }
        }
    }

    private static void WriteExceptionHandling(ICodeWriter writer, bool isHandlerAsync)
    {
        writer.Catch(
            "Blueprint.Validation.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return(WrapOperationResult("new Blueprint.Middleware.ValidationFailedOperationResult(e.ValidationResults)", isHandlerAsync));
            });

        writer.Catch(
            "System.ComponentModel.DataAnnotations.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return(WrapOperationResult("Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedOperationResult(e)", isHandlerAsync));
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
                catchWriter.Return(WrapOperationResult("new Blueprint.UnhandledExceptionOperationResult(e)", isHandlerAsync));
            });
    }

    private static string WrapOperationResult(string operationResultExpression, bool isHandlerAsync)
    {
        return isHandlerAsync
            ? operationResultExpression
            : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({operationResultExpression})";
    }
}
