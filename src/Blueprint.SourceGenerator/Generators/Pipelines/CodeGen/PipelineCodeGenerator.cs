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
            .MakeInternalClass()
            .Sealed()
            .SetBaseClass("Blueprint.OperationExecutorShell")
            .DisableWarning("CS0618");

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
            ? new[] { "loggerFactory", $"\"{handler.OperationTypeName}\"", "pipelineServices", "suppressActivity: true" }
            : new[] { "loggerFactory", $"\"{handler.OperationTypeName}\"", "pipelineServices" };

        var ctor = pipelineClass.AddConstructor()
            .AddParameter("Microsoft.Extensions.Logging.ILoggerFactory", "loggerFactory")
            .AddParameter("Blueprint.Runtime.Exec.PipelineServices", "pipelineServices");

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
                    // Alias the service provider so existing CacheService resolution expressions work unchanged
                    writer.AppendLine("var serviceProvider = pipelineServices.ServiceProvider;");

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
            .WithReturnType("System.Threading.Tasks.ValueTask<Blueprint.OperationResult>")
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
            var hasAnySteps = false;
            var hasSuccessGuardedStep = false;

            foreach (var step in plan.GetRenderSteps(contribution))
            {
                hasAnySteps = true;

                if (step.RequiresSuccessGuard)
                {
                    hasSuccessGuardedStep = true;
                    break;
                }
            }

            if (!hasAnySteps)
            {
                continue;
            }

            // When the handler result is guaranteed successful at compile time (PlainValue/NoResult),
            // success-guarded steps are rendered directly without the if wrapper — the guard is
            // proven unnecessary because failures manifest as exceptions, not non-successful results.
            if (!inSuccessGuard && hasSuccessGuardedStep)
            {
                inSuccessGuard = true;

                // Render any non-guarded steps in this contribution first
                foreach (var step in plan.GetRenderSteps(contribution))
                {
                    if (!step.RequiresSuccessGuard)
                    {
                        step.Render(new PipelineRenderContext(writer, handler, plan));
                    }
                }

                if (plan.HandlerResultAlwaysSuccessful)
                {
                    // No if guard needed — render success-guarded steps directly
                    writer.NewLine();
                    var directContext = new PipelineRenderContext(writer, handler, plan);

                    foreach (var step in plan.RenderSteps)
                    {
                        if (step.RequiresSuccessGuard)
                        {
                            step.Render(directContext);
                        }
                    }
                }
                else
                {
                    writer.NewLine();
                    writer.If(
                        "handlerResult.WasSuccessful",
                        guardWriter =>
                        {
                            var guardContext = new PipelineRenderContext(guardWriter, handler, plan);

                            foreach (var step in plan.RenderSteps)
                            {
                                if (step.RequiresSuccessGuard)
                                {
                                    step.Render(guardContext);
                                }
                            }
                        });
                }

                continue;
            }

            // Skip success-guarded steps from later contributions — already rendered above
            if (inSuccessGuard)
            {
                foreach (var step in plan.GetRenderSteps(contribution))
                {
                    if (!step.RequiresSuccessGuard)
                    {
                        step.Render(new PipelineRenderContext(writer, handler, plan));
                    }
                }

                continue;
            }

            foreach (var renderStep in plan.GetRenderSteps(contribution))
            {
                renderStep.Render(new PipelineRenderContext(writer, handler, plan));
            }
        }
    }

    /// <summary>
    /// Wraps a generated result expression so the surrounding execute method still returns a ValueTask when the pipeline is not async.
    /// </summary>
    /// <param name="operationResultExpression">The generated operation-result expression.</param>
    /// <param name="isPipelineAsync">Whether the generated pipeline method is async.</param>
    /// <returns>The expression to emit from the generated catch or handler-return path.</returns>
    internal static string WrapOperationResult(string operationResultExpression, bool isPipelineAsync)
    {
        return isPipelineAsync
            ? operationResultExpression
            : $"new System.Threading.Tasks.ValueTask<Blueprint.OperationResult>({operationResultExpression})";
    }

    /// <summary>
    /// Adds a static readonly field of applied module IDs and a static method that returns it.
    /// The field is allocated once per type load, avoiding per-call array allocation.
    /// </summary>
    private static void AddGetAppliedModulesMethod(ClassBuilder pipelineClass, PipelinePlan plan)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contribution in plan.Contributions)
        {
            seenIds.Add(contribution.ContributorId);
        }

        var arrayBuilder = new System.Text.StringBuilder();
        var first = true;
        foreach (var id in seenIds)
        {
            if (!first)
            {
                arrayBuilder.Append(", ");
            }

            arrayBuilder.Append('"').Append(id).Append('"');
            first = false;
        }

        pipelineClass.AddProperty("_appliedModuleIds")
            .SetType("string[]")
            .MakeStatic()
            .WithReadonlyValue($"[{arrayBuilder}]");

        pipelineClass.AddMethod("GetAppliedModules")
            .MakePublicMethod()
            .MakeStaticMethod()
            .WithReturnType("System.Collections.Generic.IReadOnlyList<string>")
            .WithBody(writer => writer.Return("_appliedModuleIds"));
    }
}
