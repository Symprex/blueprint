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
        writer.Block("namespace Blueprint.Generated");

        // Class declaration
        writer.Block($"public class {handler.PipelineClassName} : Blueprint.IOperationExecutorPipeline");

        // Add logging fields
        AddLoggingFields(writer);
        writer.BlankLine();

        // Add logger instance field
        writer.WriteLine("private readonly Microsoft.Extensions.Logging.ILogger _logger;");
        writer.BlankLine();

        // Add constructor
        AddConstructor(writer, handler);
        writer.BlankLine();

        // Add ExecuteAsync method
        AddExecuteAsyncMethod(writer, handler);

        writer.FinishBlock(); // End class
        writer.FinishBlock(); // End namespace

        return writer.Code();
    }

    private static void AddLoggingFields(SourceWriter writer)
    {
        // LoggerMessage.Define for structured logging - these are multi-line initializers
        writer.Indent().Append("private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception> _ApiOperationExecuting =").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Append("Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("Microsoft.Extensions.Logging.LogLevel.Debug,").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("new Microsoft.Extensions.Logging.EventId(1, \"ApiOperationExecuting\"),").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("\"Executing API operation {OperationType} with handler\");").Append(SourceWriter.NewLine);
        writer.BlankLine();

        writer.Indent().Append("private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception> _NonException =").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Append("Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("Microsoft.Extensions.Logging.LogLevel.Information,").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("new Microsoft.Extensions.Logging.EventId(4, \"Non500Exception\"),").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("\"A non-5xx ApiException has occurred with message {ExceptionMessage}\");").Append(SourceWriter.NewLine);
        writer.BlankLine();

        writer.Indent().Append("private static readonly System.Action<Microsoft.Extensions.Logging.ILogger, string, System.Exception> _UnhandledException =").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Append("Microsoft.Extensions.Logging.LoggerMessage.Define<System.String>(").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("Microsoft.Extensions.Logging.LogLevel.Error,").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("new Microsoft.Extensions.Logging.EventId(5, \"UnhandledException\"),").Append(SourceWriter.NewLine);
        writer.Indent().Indent().Indent().Append("\"An unhandled exception has occurred with message {ExceptionMessage}\");").Append(SourceWriter.NewLine);
    }

    private static void AddConstructor(SourceWriter writer, OperationHandlerModel handler)
    {
        writer.Block($"public {handler.PipelineClassName}(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)");
        writer.WriteLine($"_logger = loggerFactory.CreateLogger(\"{handler.PipelineClassName}\");");
        writer.FinishBlock();
    }

    private static void AddExecuteAsyncMethod(SourceWriter writer, OperationHandlerModel handler)
    {
        writer.Block("public async System.Threading.Tasks.Task<Blueprint.OperationResult> ExecuteAsync(Blueprint.Runtime.Exec.ApiOperationContext context)");

        // Activity tracing
        writer.WriteLine($"using var activity = Blueprint.Diagnostics.BlueprintActivitySource.ActivitySource.StartActivity(\"{handler.OperationTypeName}Pipeline\", System.Diagnostics.ActivityKind.Internal);");
        writer.WriteLine($"var operation = ({handler.OperationTypeFullName})context.Operation;");
        writer.BlankLine();

        // Try-catch block
        writer.Block("try");

        // Log execution
        writer.WriteLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
        writer.BlankLine();

        // Generate handler invocation
        GenerateHandlerInvocation(writer, handler);

        writer.FinishBlock(); // End try

        // Exception handling
        AddExceptionHandling(writer);

        writer.FinishBlock(); // End ExecuteAsync
    }

    private static void GenerateHandlerInvocation(SourceWriter writer, OperationHandlerModel handler)
    {
        // Resolve all parameters first
        foreach (var param in handler.Parameters)
        {
            switch (param.Source)
            {
                case ParameterSource.FromDependencyInjection:
                    writer.WriteLine($"var {param.VariableName} = context.ServiceProvider.GetRequiredService<{param.ParameterTypeName}>();");
                    break;
            }
        }

        if (handler.Parameters.Any(p => p.Source == ParameterSource.FromDependencyInjection))
        {
            writer.BlankLine();
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
                writer.WriteLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : string.Empty)}{handler.OperationTypeFullName}.{handler.HandlerMethodName}({string.Join(", ", parameterList)});");
            }
            else
            {
                writer.WriteLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : string.Empty)}operation.{handler.HandlerMethodName}({string.Join(", ", parameterList)});");
            }
        }
        else
        {
            // External handler - need to instantiate or get from DI
            var handlerVarName = char.ToLowerInvariant(handler.HandlerClassName![0]) + handler.HandlerClassName.Substring(1);

            writer.WriteLine($"var {handlerVarName} = context.ServiceProvider.GetService<{handler.HandlerClassFullName}>();");
            writer.Block($"if ({handlerVarName} == null)");
            writer.WriteLine($"{handlerVarName} = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{handler.HandlerClassFullName}>(context.ServiceProvider);");
            writer.FinishBlock();
            writer.BlankLine();

            // Add the operation as first parameter for external handlers
            var allParams = new List<string> { "operation" };
            allParams.AddRange(parameterList);

            writer.WriteLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : string.Empty)}{handlerVarName}.{handler.HandlerMethodName}({string.Join(", ", allParams)});");
        }

        writer.BlankLine();
        writer.WriteLine("return new Blueprint.OkResult(handlerResult);");
    }

    private static void AddExceptionHandling(SourceWriter writer)
    {
        writer.Block("catch (Blueprint.Validation.ValidationException e)");
        writer.WriteLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
        writer.WriteLine("return new Blueprint.Middleware.ValidationFailedOperationResult(e.ValidationResults);");
        writer.FinishBlock();

        writer.Block("catch (System.ComponentModel.DataAnnotations.ValidationException e)");
        writer.WriteLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
        writer.WriteLine("return Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedOperationResult(e);");
        writer.FinishBlock();

        writer.Block("catch (System.Exception e)");
        writer.Block("if (e is Blueprint.ApiException apiException && apiException.HttpStatus < 500)");
        writer.WriteLine("_NonException(_logger, e.Message, e);");
        writer.WriteLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
        writer.FinishBlock();
        writer.Block("else");
        writer.WriteLine("Blueprint.Diagnostics.BlueprintActivitySource.RecordException(context.Activity, e, false);");
        writer.WriteLine("_UnhandledException(_logger, e.Message, e);");
        writer.WriteLine("context.Activity?.SetTag(\"otel.status_code\", \"ERROR\");");
        writer.WriteLine("context.Activity?.SetTag(\"otel.status_description\", e.Message);");
        writer.FinishBlock();
        writer.BlankLine();
        writer.WriteLine("return new Blueprint.UnhandledExceptionOperationResult(e);");
        writer.FinishBlock();
    }
}
