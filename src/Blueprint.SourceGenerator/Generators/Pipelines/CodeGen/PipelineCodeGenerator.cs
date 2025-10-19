using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using CodeGenHelpers;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Generates the executor pipeline code for a single operation.
/// </summary>
internal static class PipelineCodeGenerator
{
    /// <summary>
    /// Generates the complete pipeline class for an operation handler.
    /// </summary>
    public static string GeneratePipeline(OperationHandlerModel handler)
    {
        var codeBuilder = CodeBuilder.Create("Blueprint.Generated");

        var pipelineClass = codeBuilder.AddClass(handler.PipelineClassName)
            .WithAccessModifier(Accessibility.Public)
            .Implements("Blueprint.IOperationExecutorPipeline");

        // Add logging fields
        AddLoggingFields(pipelineClass);

        // Add constructor
        AddConstructor(pipelineClass, handler);

        // Add ExecuteAsync method
        AddExecuteAsyncMethod(pipelineClass, handler);

        return codeBuilder.Build();
    }

    private static void AddLoggingFields(ClassBuilder classBuilder)
    {
        // LoggerMessage.Define for structured logging
        classBuilder.AddField("_ApiOperationExecuting")
            .MakeReadOnly()
            .MakeStatic()
            .WithAccessModifier(Accessibility.Private)
            .WithType("System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception>")
            .WithValue(@"Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(
                Microsoft.Extensions.Logging.LogLevel.Debug,
                new Microsoft.Extensions.Logging.EventId(1, ""ApiOperationExecuting""),
                ""Executing API operation {OperationType} with handler"")");

        classBuilder.AddField("_NonException")
            .MakeReadOnly()
            .MakeStatic()
            .WithAccessModifier(Accessibility.Private)
            .WithType("System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception>")
            .WithValue(@"Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(
                Microsoft.Extensions.Logging.LogLevel.Information,
                new Microsoft.Extensions.Logging.EventId(4, ""Non500Exception""),
                ""A non-5xx ApiException has occurred with message {ExceptionMessage}"")");

        classBuilder.AddField("_UnhandledException")
            .MakeReadOnly()
            .MakeStatic()
            .WithAccessModifier(Accessibility.Private)
            .WithType("System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception>")
            .WithValue(@"Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(
                Microsoft.Extensions.Logging.LogLevel.Error,
                new Microsoft.Extensions.Logging.EventId(5, ""UnhandledException""),
                ""An unhandled exception has occurred with message {ExceptionMessage}"")");

        // Logger instance
        classBuilder.AddField("_logger")
            .MakeReadOnly()
            .WithAccessModifier(Accessibility.Private)
            .WithType("Microsoft.Extensions.Logging.ILogger");
    }

    private static void AddConstructor(ClassBuilder classBuilder, OperationHandlerModel handler)
    {
        classBuilder.AddConstructor()
            .WithAccessModifier(Accessibility.Public)
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .WithBody(b =>
            {
                b.AppendLine($"_logger = loggerFactory.CreateLogger(\"{handler.PipelineClassName}\");");
            });
    }

    private static void AddExecuteAsyncMethod(ClassBuilder classBuilder, OperationHandlerModel handler)
    {
        classBuilder.AddMethod("ExecuteAsync")
            .WithAccessModifier(Accessibility.Public)
            .WithReturnType("async System.Threading.Tasks.Task<Blueprint.OperationResult>")
            .AddParameter("Blueprint.Runtime.Exec.ApiOperationContext", "context")
            .WithBody(b =>
            {
                // Activity tracing
                b.AppendLine($"using var activity = Blueprint.Diagnostics.BlueprintActivitySource.ActivitySource.StartActivity(\"{handler.OperationTypeName}Pipeline\", System.Diagnostics.ActivityKind.Internal);");
                b.AppendLine($"var operation = ({handler.OperationTypeFullName})context.Operation;");
                b.AppendLine();

                // Try-catch block
                b.AppendLine("try");
                b.AppendLine("{");
                b.Indent();

                // Log execution
                b.AppendLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
                b.AppendLine();

                // TODO: Inject handler invocation code
                b.AppendLine("// Handler invocation will be generated here");
                b.AppendLine($"throw new System.NotImplementedException(\"Pipeline for {handler.OperationTypeName} not yet implemented\");");

                b.Unindent();
                b.AppendLine("}");

                // Exception handling
                AddExceptionHandling(b);

                b.AppendLine();
                b.AppendLine("return new Blueprint.OkResult(null);");
            });
    }

    private static void AddExceptionHandling(ICodeWriter writer)
    {
        writer.AppendLine("catch (Blueprint.Validation.ValidationException e)");
        writer.AppendLine("{");
        writer.Indent();
        writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
        writer.AppendLine("return new Blueprint.Middleware.ValidationFailedOperationResult(e.ValidationResults);");
        writer.Unindent();
        writer.AppendLine("}");

        writer.AppendLine("catch (System.ComponentModel.DataAnnotations.ValidationException e)");
        writer.AppendLine("{");
        writer.Indent();
        writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
        writer.AppendLine("return Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedOperationResult(e);");
        writer.Unindent();
        writer.AppendLine("}");

        writer.AppendLine("catch (System.Exception e)");
        writer.AppendLine("{");
        writer.Indent();
        writer.AppendLine("if (e is Blueprint.ApiException apiException && apiException.HttpStatus < 500)");
        writer.AppendLine("{");
        writer.Indent();
        writer.AppendLine("_NonException(_logger, e.Message, e);");
        writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
        writer.Unindent();
        writer.AppendLine("}");
        writer.AppendLine("else");
        writer.AppendLine("{");
        writer.Indent();
        writer.AppendLine("Blueprint.Diagnostics.BlueprintActivitySource.RecordException(context.Activity, e, false);");
        writer.AppendLine("_UnhandledException(_logger, e.Message, e);");
        writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"ERROR\");");
        writer.AppendLine("context.Activity?.SetTag(\"otel.status_description\", e.Message);");
        writer.Unindent();
        writer.AppendLine("}");
        writer.AppendLine("return new Blueprint.UnhandledExceptionOperationResult(e);");
        writer.Unindent();
        writer.AppendLine("}");
    }
}
