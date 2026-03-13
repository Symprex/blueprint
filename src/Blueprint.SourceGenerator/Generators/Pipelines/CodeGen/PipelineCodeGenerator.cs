using System.Collections.Generic;
using System.Linq;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;

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
            .MakePublicClass()
            .AddInterface("Blueprint.IOperationExecutorPipeline");

        AddLoggingFields(pipelineClass);
        AddLoggerField(pipelineClass);
        AddConstructor(pipelineClass, handler);
        AddExecuteAsyncMethod(pipelineClass, handler);

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
            .WithBody(writer => writer.AppendLine($"_logger = loggerFactory.CreateLogger(\"{handler.PipelineClassName}\");"));
    }

    private static void AddExecuteAsyncMethod(ClassBuilder pipelineClass, OperationHandlerModel handler)
    {
        pipelineClass.AddMethod("ExecuteAsync")
            .MakePublicMethod()
            .MakeAsync()
            .WithReturnType("System.Threading.Tasks.Task<Blueprint.OperationResult>")
            .AddParameter("Blueprint.Runtime.Exec.ApiOperationContext", "context")
            .WithBody(
                writer =>
                {
                    writer.AppendLine($"using var activity = Blueprint.Diagnostics.BlueprintActivitySource.ActivitySource.StartActivity(\"{handler.OperationTypeName}Pipeline\", System.Diagnostics.ActivityKind.Internal);");
                    writer.AppendLine($"var operation = ({handler.OperationTypeFullName})context.Operation;");
                    writer.NewLine();

                    using (writer.Block("try"))
                    {
                        writer.AppendLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
                        writer.NewLine();

                        WriteHandlerInvocation(writer, handler);
                    }

                    WriteExceptionHandling(writer);
                });
    }

    private static void WriteHandlerInvocation(ICodeWriter writer, OperationHandlerModel handler)
    {
        foreach (var param in handler.Parameters)
        {
            switch (param.Source)
            {
                case ParameterSource.FromDependencyInjection:
                    writer.AppendLine($"var {param.VariableName} = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{param.ParameterTypeName}>(context.ServiceProvider);");
                    break;
            }
        }

        if (handler.Parameters.Any(p => p.Source == ParameterSource.FromDependencyInjection))
        {
            writer.NewLine();
        }

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

        if (handler.IsInline)
        {
            if (handler.IsHandlerStatic)
            {
                writer.AppendLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : string.Empty)}{handler.OperationTypeFullName}.{handler.HandlerMethodName}({string.Join(", ", parameterList)});");
            }
            else
            {
                writer.AppendLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : string.Empty)}operation.{handler.HandlerMethodName}({string.Join(", ", parameterList)});");
            }
        }
        else
        {
            var handlerVarName = char.ToLowerInvariant(handler.HandlerClassName![0]) + handler.HandlerClassName.Substring(1);

            writer.AppendLine($"var {handlerVarName} = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{handler.HandlerClassFullName}>(context.ServiceProvider);");
            using (writer.Block($"if ({handlerVarName} == null)"))
            {
                writer.AppendLine($"{handlerVarName} = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{handler.HandlerClassFullName}>(context.ServiceProvider);");
            }

            writer.NewLine();

            var allParams = new List<string> { "operation" };
            allParams.AddRange(parameterList);

            writer.AppendLine($"var handlerResult = {(handler.IsHandlerAsync ? "await " : string.Empty)}{handlerVarName}.{handler.HandlerMethodName}({string.Join(", ", allParams)});");
        }

        writer.NewLine();
        writer.AppendLine("return new Blueprint.Runtime.Exec.OkResult(handlerResult);");
    }

    private static void WriteExceptionHandling(ICodeWriter writer)
    {
        using (writer.Block("catch (Blueprint.Validation.ValidationException e)"))
        {
            writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
            writer.AppendLine("return new Blueprint.Middleware.ValidationFailedOperationResult(e.ValidationResults);");
        }

        using (writer.Block("catch (System.ComponentModel.DataAnnotations.ValidationException e)"))
        {
            writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
            writer.AppendLine("return Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedOperationResult(e);");
        }

        using (writer.Block("catch (System.Exception e)"))
        {
            using (writer.Block("if (e is Blueprint.ApiException apiException && apiException.HttpStatus < 500)"))
            {
                writer.AppendLine("_NonException(_logger, e.Message, e);");
                writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
            }

            using (writer.Block("else"))
            {
                writer.AppendLine("Blueprint.Diagnostics.BlueprintActivitySource.RecordException(context.Activity, e, false);");
                writer.AppendLine("_UnhandledException(_logger, e.Message, e);");
                writer.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"ERROR\");");
                writer.AppendLine("context.Activity?.SetTag(\"otel.status_description\", e.Message);");
            }

            writer.NewLine();
            writer.AppendLine("return new Blueprint.UnhandledExceptionOperationResult(e);");
        }
    }
}
