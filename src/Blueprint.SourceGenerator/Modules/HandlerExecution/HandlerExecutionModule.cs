using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Routes handler invocation through the shared pipeline contributor seam.
/// </summary>
public sealed class HandlerExecutionModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    public const string HandlerResultPurpose = "handler-result";

    public string ModuleId => nameof(HandlerExecutionModule);

    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    public string ContributorId => nameof(HandlerExecutionModule);

    public int Order => 0;

    public bool IsUnique => true;

    public IReadOnlyList<string> DependsOn { get; } = [];

    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var handler = context.Handler ?? throw new InvalidOperationException("The pipeline contributor requires handler metadata, but no handler was resolved for the current operation.");

        context.AddRenderStep(PipelineStage.Execution, RenderHandlerInvocation);
        context.RegisterVariable(
            new VariableKey(GetOperationResultTypeNameOrFallback(context.Operation, handler.HandlerReturnTypeName), HandlerResultPurpose),
            availableFrom: PipelineStage.Execution,
            sourceKind: VariableSourceKind.ProducedValue,
            lifetime: VariableLifetime.Local);
    }

    private static void RenderHandlerInvocation(PipelineRenderContext context)
    {
        foreach (var param in context.GetHandlerParameters())
        {
            switch (param.Source)
            {
                case ParameterSource.FromDependencyInjection:
                    context.DeclareVariable(
                        param.VariableName,
                        context.GetRequiredServiceExpression(param.ParameterTypeName));
                    break;
            }
        }

        if (context.GetHandlerParameters().Any(p => p.Source == ParameterSource.FromDependencyInjection))
        {
            context.WriteBlankLine();
        }

        var parameterList = new List<string>();
        foreach (var param in context.GetHandlerParameters())
        {
            switch (param.Source)
            {
                case ParameterSource.Operation:
                    parameterList.Add(context.GetOperationExpression());
                    break;
                case ParameterSource.FromContext:
                    parameterList.Add(context.GetContextExpression());
                    break;
                case ParameterSource.FromContextProperty:
                    parameterList.Add(context.GetContextPropertyExpression(param.ContextPropertyName!));
                    break;
                case ParameterSource.FromDependencyInjection:
                    parameterList.Add(param.VariableName);
                    break;
            }
        }

        if (context.IsInlineHandler())
        {
            if (context.IsStaticHandler())
            {
                context.DeclareVariable("handlerResult", $"{(context.IsHandlerAsync() ? "await " : string.Empty)}{context.GetOperationTypeFullName()}.{context.GetHandlerMethodName()}({string.Join(", ", parameterList)})");
            }
            else
            {
                context.DeclareVariable("handlerResult", $"{(context.IsHandlerAsync() ? "await " : string.Empty)}{context.GetOperationExpression()}.{context.GetHandlerMethodName()}({string.Join(", ", parameterList)})");
            }
        }
        else
        {
            var handlerClassName = context.GetRequiredHandlerClassName();
            var handlerClassFullName = context.GetRequiredHandlerClassFullName();
            var handlerVarName = char.ToLowerInvariant(handlerClassName[0]) + handlerClassName.Substring(1);

            context.DeclareVariable(
                handlerVarName,
                context.GetOptionalServiceExpression(handlerClassFullName));
            context.If(
                $"{handlerVarName} == null",
                branch =>
                {
                    branch.Assign(
                        handlerVarName,
                        branch.GetActivatorCreateInstanceExpression(handlerClassFullName));
                });

            context.WriteBlankLine();

            var allParams = new List<string> { context.GetOperationExpression() };
            allParams.AddRange(parameterList);

            context.DeclareVariable("handlerResult", $"{(context.IsHandlerAsync() ? "await " : string.Empty)}{handlerVarName}.{context.GetHandlerMethodName()}({string.Join(", ", allParams)})");
        }

        context.WriteBlankLine();
        context.Return(WrapOperationResult("new Blueprint.Runtime.Exec.OkResult(handlerResult)", context.IsHandlerAsync()));
    }

    private static string GetOperationResultTypeNameOrFallback(OperationModel operation, string fallbackTypeName)
    {
        var operationResultInterface = operation.TypeSymbol.AllInterfaces.FirstOrDefault(
            candidate => candidate.TypeArguments.Length == 1
                && candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
                && (candidate.Name == "ICommand" || candidate.Name == "IQuery" || candidate.Name == "IOperation"));

        return operationResultInterface is null
            ? fallbackTypeName
            : operationResultInterface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string WrapOperationResult(string operationResultExpression, bool isHandlerAsync)
    {
        return isHandlerAsync
            ? operationResultExpression
            : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({operationResultExpression})";
    }
}
