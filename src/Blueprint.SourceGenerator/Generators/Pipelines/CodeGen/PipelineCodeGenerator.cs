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
            .WithBody(writer => writer.Assign("_logger", $"loggerFactory.CreateLogger(\"{handler.PipelineClassName}\")"));
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
                    writer.DeclareVar("operation", $"({handler.OperationTypeFullName})context.Operation");
                    writer.NewLine();

                    writer.Try(
                        tryWriter =>
                        {
                            tryWriter.AppendLine($"_ApiOperationExecuting(_logger, \"{handler.OperationTypeName}\", null);");
                            tryWriter.NewLine();
                            WriteHandlerInvocation(tryWriter, handler);
                        });

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
                    writer.DeclareVar(
                        param.VariableName,
                        $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{param.ParameterTypeName}>(context.ServiceProvider)");
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
                writer.DeclareVar("handlerResult", $"{(handler.IsHandlerAsync ? "await " : string.Empty)}{handler.OperationTypeFullName}.{handler.HandlerMethodName}({string.Join(", ", parameterList)})");
            }
            else
            {
                writer.DeclareVar("handlerResult", $"{(handler.IsHandlerAsync ? "await " : string.Empty)}operation.{handler.HandlerMethodName}({string.Join(", ", parameterList)})");
            }
        }
        else
        {
            var handlerVarName = char.ToLowerInvariant(handler.HandlerClassName![0]) + handler.HandlerClassName.Substring(1);

            writer.DeclareVar(
                handlerVarName,
                $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{handler.HandlerClassFullName}>(context.ServiceProvider)");
            writer.If(
                $"{handlerVarName} == null",
                ifWriter =>
                {
                    ifWriter.Assign(
                        handlerVarName,
                        $"Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{handler.HandlerClassFullName}>(context.ServiceProvider)");
                });

            writer.NewLine();

            var allParams = new List<string> { "operation" };
            allParams.AddRange(parameterList);

            writer.DeclareVar("handlerResult", $"{(handler.IsHandlerAsync ? "await " : string.Empty)}{handlerVarName}.{handler.HandlerMethodName}({string.Join(", ", allParams)})");
        }

        writer.NewLine();
        writer.Return("new Blueprint.Runtime.Exec.OkResult(handlerResult)");
    }

    private static void WriteExceptionHandling(ICodeWriter writer)
    {
        writer.Catch(
            "Blueprint.Validation.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return("new Blueprint.Middleware.ValidationFailedOperationResult(e.ValidationResults)");
            });

        writer.Catch(
            "System.ComponentModel.DataAnnotations.ValidationException e",
            catchWriter =>
            {
                catchWriter.AppendLine("context.Activity?.SetTag(\"otel.status_code\", \"OK\");");
                catchWriter.Return("Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedOperationResult(e)");
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
                catchWriter.Return("new Blueprint.UnhandledExceptionOperationResult(e)");
            });
    }
}
