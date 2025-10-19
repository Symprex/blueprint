using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;

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
        var writer = new SourceWriter();

        // Namespace
        writer.AppendLine("namespace Blueprint.Generated");
        writer.AppendLine("{");
        writer.Indent();

        // Class declaration
        writer.AppendLine($"public class {handler.PipelineClassName} : Blueprint.IOperationExecutorPipeline");
        writer.AppendLine("{");
        writer.Indent();

        // Add logging fields
        AddLoggingFields(writer);
        writer.AppendLine();

        // Add logger instance field
        writer.AppendLine("private readonly Microsoft.Extensions.Logging.ILogger _logger;");
        writer.AppendLine();

        // Add constructor
        AddConstructor(writer, handler);
        writer.AppendLine();

        // Add ExecuteAsync method
        AddExecuteAsyncMethod(writer, handler);

        writer.Unindent();
        writer.AppendLine("}");

        writer.Unindent();
        writer.AppendLine("}");

        return writer.ToString();
    }

    private static void AddLoggingFields(SourceWriter writer)
    {
        // LoggerMessage.Define for structured logging
        writer.AppendLine("private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception> _ApiOperationExecuting =");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.LogLevel.Debug,");
        writer.AppendLine("new Microsoft.Extensions.Logging.EventId(1, \"ApiOperationExecuting\"),");
        writer.AppendLine("\"Executing API operation {OperationType} with handler\");");
        writer.Unindent();
        writer.Unindent();
        writer.AppendLine();

        writer.AppendLine("private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception> _NonException =");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.LogLevel.Information,");
        writer.AppendLine("new Microsoft.Extensions.Logging.EventId(4, \"Non500Exception\"),");
        writer.AppendLine("\"A non-5xx ApiException has occurred with message {ExceptionMessage}\");");
        writer.Unindent();
        writer.Unindent();
        writer.AppendLine();

        writer.AppendLine("private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception> _UnhandledException =");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(");
        writer.Indent();
        writer.AppendLine("Microsoft.Extensions.Logging.LogLevel.Error,");
        writer.AppendLine("new Microsoft.Extensions.Logging.EventId(5, \"UnhandledException\"),");
        writer.AppendLine("\"An unhandled exception has occurred with message {ExceptionMessage}\");");
        writer.Unindent();
        writer.Unindent();
    }

    private static void AddConstructor(SourceWriter writer, OperationHandlerModel handler)
    {
        writer.AppendLine($"public {handler.PipelineClassName}(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)");
        writer.AppendLine("{");
        writer.Indent();
        writer.AppendLine($"_logger = loggerFactory.CreateLogger(\"{handler.PipelineClassName}\");");
        writer.Unindent();
        writer.AppendLine("}");
    }

    private static void AddExecuteAsyncMethod(SourceWriter writer, OperationHandlerModel handler)
    {
        writer.AppendLine("public async System.Threading.Tasks.Task<Blueprint.OperationResult> ExecuteAsync(Blueprint.Runtime.Exec.ApiOperationContext context)");
        writer.AppendLine("{");
        writer.Indent();

        // Activity tracing
        writer.AppendLine($"using var activity = Blueprint.Diagnostics.BlueprintActivitySource.ActivitySource.StartActivity(\"{handler.OperationTypeName}Pipeline\", System.Diagnostics.ActivityKind.Internal);");
        writer.AppendLine($"var operation = ({handler.OperationTypeFullName})context.Operation;");
        writer.AppendLine();

        // Try-catch block
        writer.AppendLine("try");
        writer.AppendLine("{");
        writer.Indent();

        // Log execution
        writer.AppendLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
        writer.AppendLine();

        // Generate handler invocation
        GenerateHandlerInvocation(writer, handler);

        writer.Unindent();
        writer.AppendLine("}");

        // Exception handling
        AddExceptionHandling(writer);

        writer.Unindent();
        writer.AppendLine("}");
    }

    private static void GenerateHandlerInvocation(SourceWriter writer, OperationHandlerModel handler)
    {
        // Resolve all parameters first
        foreach (var param in handler.Parameters)
        {
            switch (param.Source)
            {
                case ParameterSource.FromDependencyInjection:
                    writer.AppendLine($"var {param.VariableName} = context.ServiceProvider.GetRequiredService<{param.ParameterTypeName}>();");
                    break;
            }
        }

        if (handler.Parameters.Any(p => p.Source == ParameterSource.FromDependencyInjection))
        {
            writer.AppendLine();
        }

        // Build parameter list
        var parameterList = new List<string>();
        foreach (var param in handler.Parameters)
        {
            switch (param.Source)
            {
                case ParameterSource.Operation:
                    parameterList.Add("operation");
                    break;
                case ParameterSource.FromContext:
                    parameterList.Add("context");
                    break;
                case ParameterSource.FromContextProperty:
                    parameterList.Add($"context.{param.ContextPropertyName}");
                    break;
                case ParameterSource.FromDependencyInjection:
                    parameterList.Add(param.VariableName);
                    break;
            }
        }

        // Generate invocation based on handler type
        if (handler.IsInline)
        {
            // Inline handler
            if (handler.IsHandlerStatic)
            {
                writer.AppendLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : "")}{handler.OperationTypeFullName}.{handler.HandlerMethodName}({string.Join(", ", parameterList)});");
            }
            else
            {
                writer.AppendLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : "")}operation.{handler.HandlerMethodName}({string.Join(", ", parameterList)});");
            }
        }
        else
        {
            // External handler - need to instantiate or get from DI
            var handlerVarName = char.ToLowerInvariant(handler.HandlerClassName![0]) + handler.HandlerClassName.Substring(1);
            
            writer.AppendLine($"var {handlerVarName} = context.ServiceProvider.GetService<{handler.HandlerClassFullName}>();");
            writer.AppendLine($"if ({handlerVarName} == null)");
            writer.AppendLine("{");
            writer.Indent();
            writer.AppendLine($"{handlerVarName} = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{handler.HandlerClassFullName}>(context.ServiceProvider);");
            writer.Unindent();
            writer.AppendLine("}");
            writer.AppendLine();

            // Add the operation as first parameter for external handlers
            var allParams = new List<string> { "operation" };
            allParams.AddRange(parameterList);

            writer.AppendLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : "")}{handlerVarName}.{handler.HandlerMethodName}({string.Join(", ", allParams)});");
        }

        writer.AppendLine();
        writer.AppendLine("return new Blueprint.OkResult(handlerResult);");
    }

    private static void AddExceptionHandling(SourceWriter writer)
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
        writer.AppendLine();
        writer.AppendLine("return new Blueprint.UnhandledExceptionOperationResult(e);");
        writer.Unindent();
        writer.AppendLine("}");
    }
}
