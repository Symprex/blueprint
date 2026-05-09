using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Generates the executor pipeline code for a single operation. The generated class extends
/// <c>Blueprint.OperationExecutorShell</c> which owns activity lifecycle, structured logging,
/// and exception translation. The generated code only contains the operation-specific
/// <c>ExecuteCoreAsync</c> override plus any deferred class members (e.g. PopulateFromJsonBody).
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
            .Sealed()
            .SetBaseClass("Blueprint.OperationExecutorShell");

        AddConstructor(pipelineClass, handler, plan);
        AddExecuteCoreAsyncMethod(pipelineClass, handler, plan);
        AddGetAppliedModulesMethod(pipelineClass, plan);

        foreach (var member in plan.DeferredClassMembers)
        {
            member(pipelineClass);
        }

        return codeBuilder.Build();
    }

    private static void AddConstructor(ClassBuilder pipelineClass, OperationHandlerModel handler, PipelinePlan plan)
    {
        var baseCallArgs = plan.SuppressActivity
            ? new[] { "loggerFactory", $"\"{handler.OperationTypeName}\"", "suppressActivity: true" }
            : new[] { "loggerFactory", $"\"{handler.OperationTypeName}\"" };

        pipelineClass.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .WithBaseCallArguments(baseCallArgs);
    }

    private static void AddExecuteCoreAsyncMethod(
        ClassBuilder pipelineClass,
        OperationHandlerModel handler,
        PipelinePlan plan)
    {
        var executeMethod = pipelineClass.AddMethod("ExecuteCoreAsync")
            .MakeProtectedMethod()
            .Override()
            .WithReturnType("System.Threading.Tasks.Task<Blueprint.OperationResult>")
            .AddParameter("Blueprint.Runtime.Exec.ApiOperationContext", "context");

        if (handler.IsHandlerAsync || plan.RequiresAsync)
        {
            executeMethod.MakeAsync();
        }

        executeMethod.WithBody(
            writer =>
            {
                writer.DeclareVar("operation", $"({handler.OperationTypeFullName})context.Operation");
                writer.NewLine();
                WritePipelineSteps(writer, handler, plan);
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

    /// <summary>
    /// Wraps a generated result expression so the surrounding execute method still returns a task when the pipeline is not async.
    /// </summary>
    /// <param name="operationResultExpression">The generated operation-result expression.</param>
    /// <param name="isPipelineAsync">Whether the generated pipeline method is async.</param>
    /// <returns>The expression to emit from the generated catch or handler-return path.</returns>
    internal static string WrapOperationResult(string operationResultExpression, bool isPipelineAsync)
    {
        return isPipelineAsync
            ? operationResultExpression
            : $"System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>({operationResultExpression})";
    }

    /// <summary>
    /// Adds a static <c>GetAppliedModules()</c> method that returns the ordered, distinct
    /// contributor IDs from the pipeline plan. This provides a compile-time inspection
    /// surface for debugging, diagnostics, and AI-friendly manifest generation.
    /// </summary>
    private static void AddGetAppliedModulesMethod(ClassBuilder pipelineClass, PipelinePlan plan)
    {
        var contributorIds = plan.Contributions
            .Select(static c => c.ContributorId)
            .Distinct()
            .ToArray();

        var arrayElements = string.Join(", ", contributorIds.Select(static id => $"\"{id}\""));

        // The applied modules list is a diagnostic/inspection-only surface, not a hot path.
        // A static readonly backing field would be ideal but CodeWriter lacks AddField;
        // the per-call allocation is acceptable for this cold path.
        pipelineClass.AddMethod("GetAppliedModules")
            .MakePublicMethod()
            .MakeStaticMethod()
            .WithReturnType("System.Collections.Generic.IReadOnlyList<string>")
            .WithBody(writer => writer.Return($"new string[] {{ {arrayElements} }}"));
    }
}
