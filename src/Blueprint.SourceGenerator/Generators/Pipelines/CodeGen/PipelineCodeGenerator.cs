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
            ? new[] { "loggerFactory", $"\"{handler.OperationTypeName}\"", "serviceProvider", "suppressActivity: true" }
            : new[] { "loggerFactory", $"\"{handler.OperationTypeName}\"", "serviceProvider" };

        var ctor = pipelineClass.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .AddParameter("System.IServiceProvider", "serviceProvider");

        if (plan.CachedServices.Count > 0)
        {
            // Emit private readonly fields for each cached service
            foreach (var cached in plan.CachedServices.Values)
            {
                var fieldType = cached.ServiceTypeName;
                pipelineClass.AddProperty(cached.FieldName)
                    .SetType(fieldType)
                    .WithReadonlyValue();
            }

            ctor.WithBaseCallArguments(baseCallArgs)
                .WithBody(writer =>
                {
                    foreach (var cached in plan.CachedServices.Values)
                    {
                        writer.AppendLine($"this.{cached.FieldName} = {cached.ResolutionExpression};");
                    }
                });
        }
        else
        {
            ctor.WithBaseCallArguments(baseCallArgs);
        }
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
        var inSuccessGuard = false;

        foreach (var contribution in plan.Contributions)
        {
            var steps = plan.GetRenderSteps(contribution).ToList();
            if (steps.Count == 0)
            {
                continue;
            }

            // When we encounter the first success-guarded step, open a consolidated
            // if (handlerResult.WasSuccessful) block and render ALL success-guarded
            // steps from every contribution inside it.
            if (!inSuccessGuard && steps.Any(static s => s.RequiresSuccessGuard))
            {
                inSuccessGuard = true;

                // Render any non-guarded steps in this contribution first
                foreach (var step in steps.Where(static s => !s.RequiresSuccessGuard))
                {
                    step.Render(new PipelineRenderContext(writer, handler, plan));
                }

                // Collect ALL success-guarded steps across ALL remaining contributions
                var guardedSteps = plan.RenderSteps.Where(static s => s.RequiresSuccessGuard);

                writer.NewLine();
                writer.If(
                    "handlerResult.WasSuccessful",
                    guardWriter =>
                    {
                        var guardContext = new PipelineRenderContext(guardWriter, handler, plan);
                        foreach (var guardedStep in guardedSteps)
                        {
                            guardedStep.Render(guardContext);
                        }
                    });

                continue;
            }

            // Skip success-guarded steps from later contributions — already rendered above
            if (inSuccessGuard)
            {
                foreach (var step in steps.Where(static s => !s.RequiresSuccessGuard))
                {
                    step.Render(new PipelineRenderContext(writer, handler, plan));
                }

                continue;
            }

            foreach (var renderStep in steps)
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
    /// Adds a static readonly field of applied module IDs and a static method that returns it.
    /// The field is allocated once per type load, avoiding per-call array allocation.
    /// </summary>
    private static void AddGetAppliedModulesMethod(ClassBuilder pipelineClass, PipelinePlan plan)
    {
        var contributorIds = plan.Contributions
            .Select(static c => c.ContributorId)
            .Distinct()
            .ToArray();

        var arrayElements = string.Join(", ", contributorIds.Select(static id => $"\"{id}\""));

        pipelineClass.AddProperty("_appliedModuleIds")
            .SetType("string[]")
            .MakeStatic()
            .WithReadonlyValue($"new string[] {{ {arrayElements} }}");

        pipelineClass.AddMethod("GetAppliedModules")
            .MakePublicMethod()
            .MakeStaticMethod()
            .WithReturnType("System.Collections.Generic.IReadOnlyList<string>")
            .WithBody(writer => writer.Return("_appliedModuleIds"));
    }
}
