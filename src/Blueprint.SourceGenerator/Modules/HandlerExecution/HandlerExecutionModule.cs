using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Routes handler invocation through the shared pipeline contributor seam.
/// </summary>
public sealed class HandlerExecutionModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <summary>
    /// The shared variable purpose used when downstream contributors need the handler result.
    /// </summary>
    public const string HandlerResultPurpose = "handler-result";

    /// <inheritdoc />
    public string ModuleId => nameof(HandlerExecutionModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HandlerExecutionModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
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

    /// <summary>
    /// Prefers the declared operation result contract so downstream variable matching follows the public CQRS surface
    /// instead of incidental handler implementation details.
    /// </summary>
    /// <param name="operation">The operation being planned.</param>
    /// <param name="fallbackTypeName">The handler return type to use when no operation contract result is declared.</param>
    /// <returns>The operation result type name that should back the produced handler-result variable.</returns>
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

    /// <summary>
    /// Normalises synchronous and asynchronous handler paths so generated pipelines always return a
    /// <see cref="Task{TResult}" /> of <c>Blueprint.OperationResult</c>.
    /// </summary>
    /// <param name="operationResultExpression">The operation-result expression to return.</param>
    /// <param name="isHandlerAsync">Whether the handler already returns an asynchronous result.</param>
    /// <returns>A return expression compatible with the generated pipeline signature.</returns>
    private static string WrapOperationResult(string operationResultExpression, bool isHandlerAsync)
    {
        return isHandlerAsync
            ? operationResultExpression
            : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({operationResultExpression})";
    }
}
