using System.Diagnostics.CodeAnalysis;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable pipeline planning state for a single operation.
/// </summary>
/// <param name="Operation">The operation being planned.</param>
/// <param name="VariableRegistry">The variable registry for the plan.</param>
public sealed record PipelinePlan(OperationModel Operation, VariableRegistry VariableRegistry)
{
    private readonly List<DiagnosticInfo> _diagnostics = new(4);
    private readonly HashSet<PipelineContribution> _contributionSet = [];
    private readonly List<PipelineContribution> _contributions = new(8);
    private readonly List<PipelineRenderStep> _renderSteps = new(8);
    private readonly List<Action<ClassBuilder>> _deferredClassMembers = new(4);
    private readonly HashSet<string> _declaredSharedVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedServiceRegistration> _cachedServices = new(8, StringComparer.Ordinal);
    private Dictionary<PipelineContribution, List<PipelineRenderStep>>? _renderStepIndex;

    /// <summary>
    /// Declares a shared pipeline variable so that multiple modules can emit the same variable
    /// declaration exactly once. Returns <c>true</c> the first time a given name is declared,
    /// and <c>false</c> on subsequent calls — callers should only emit the declaration when
    /// this method returns <c>true</c>.
    /// </summary>
    /// <param name="variableName">The shared variable name.</param>
    /// <returns><c>true</c> if this is the first declaration; <c>false</c> if already declared.</returns>
    public bool TryDeclareSharedVariable(string variableName)
    {
        return this._declaredSharedVariables.Add(variableName);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the generated pipeline must be async
    /// even when the handler itself is synchronous (for example when the Population stage
    /// needs to read the request body asynchronously).
    /// </summary>
    public bool RequiresAsync { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated pipeline should suppress Activity
    /// creation in <c>OperationExecutorShell</c>. When <c>true</c>, the operation will not
    /// produce a trace span — useful for noisy or high-frequency operations where per-invocation
    /// tracing would create excessive telemetry noise without diagnostic value.
    /// </summary>
    public bool SuppressActivity { get; set; }

    /// <summary>
    /// Gets the accumulated diagnostics.
    /// </summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics => this._diagnostics;

    /// <summary>
    /// Gets the ordered pipeline contributions.
    /// </summary>
    public IReadOnlyList<PipelineContribution> Contributions => this._contributions;

    /// <summary>
    /// Gets the ordered pipeline render steps.
    /// </summary>
    public IReadOnlyList<PipelineRenderStep> RenderSteps => this._renderSteps;

    /// <summary>
    /// Gets the cached service registrations that should be resolved once in the pipeline constructor
    /// and stored as private readonly fields. Modules use this for services that are guaranteed to be
    /// singletons (e.g. <c>IPropertyPopulator[]</c>, <c>IBackgroundTaskScheduler</c>) to eliminate
    /// per-request <c>GetService</c> calls.
    /// </summary>
    public IReadOnlyDictionary<string, CachedServiceRegistration> CachedServices => this._cachedServices;

    /// <summary>
    /// Registers a service to be pre-resolved from the root <c>IServiceProvider</c> at pipeline
    /// construction time and cached as a private readonly field. The field is available to render
    /// steps via its <paramref name="fieldName"/>. Duplicate registrations (same field name) are
    /// silently ignored — allowing multiple modules to depend on the same cached service safely.
    /// </summary>
    /// <param name="fieldName">The private field name (e.g. <c>_propertyPopulators</c>).</param>
    /// <param name="serviceTypeName">The fully-qualified service type to resolve (e.g. <c>Blueprint.Runtime.Middleware.IPropertyPopulator[]</c>).</param>
    /// <param name="resolutionExpression">
    /// The C# expression used to resolve the service from <c>serviceProvider</c>. The expression
    /// can reference <c>serviceProvider</c> which will be in scope in the constructor.
    /// </param>
    /// <param name="isNullable">Whether the resolved service may be null (affects field type annotation).</param>
    public void CacheService(string fieldName, string serviceTypeName, string resolutionExpression, bool isNullable = false)
    {
        if (!this._cachedServices.ContainsKey(fieldName))
        {
            this._cachedServices[fieldName] = new CachedServiceRegistration(fieldName, serviceTypeName, resolutionExpression, isNullable);
        }
    }

    /// <summary>
    /// Tries to find a cached service field whose service type matches <paramref name="serviceTypeName"/>.
    /// Returns the <c>this._fieldName</c> expression when found, so callers can use the cached
    /// field instead of a per-request <c>GetRequiredService</c> call.
    /// </summary>
    /// <param name="serviceTypeName">The fully qualified service type to look up.</param>
    /// <param name="fieldExpression">When found, the <c>this._fieldName</c> expression; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a matching cached service exists.</returns>
    public bool TryGetCachedServiceFieldExpression(string serviceTypeName, [NotNullWhen(true)] out string? fieldExpression)
    {
        foreach (var entry in this._cachedServices.Values)
        {
            if (string.Equals(entry.ServiceTypeName, serviceTypeName, StringComparison.Ordinal))
            {
                fieldExpression = $"this.{entry.FieldName}";
                return true;
            }
        }

        fieldExpression = null;
        return false;
    }

    /// <summary>
    /// Adds a diagnostic to the pipeline plan.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        this._diagnostics.Add(diagnostic);
    }

    /// <summary>
    /// Ensures the contributor is registered for the given stage.
    /// </summary>
    /// <param name="stage">The target stage.</param>
    /// <param name="contributorId">The contributing contributor identifier.</param>
    public void EnsureContribution(PipelineStage stage, string contributorId)
    {
        var contribution = new PipelineContribution(stage, contributorId);

        if (!this._contributionSet.Add(contribution))
        {
            return;
        }

        var index = this._contributions.FindIndex(existing => existing.Stage > contribution.Stage);
        if (index < 0)
        {
            this._contributions.Add(contribution);
            return;
        }

        this._contributions.Insert(index, contribution);
    }

    /// <summary>
    /// Adds a render step to the pipeline plan.
    /// </summary>
    /// <param name="renderStep">The render step to add.</param>
    public void AddRenderStep(PipelineRenderStep renderStep)
    {
        this.EnsureContribution(renderStep.Stage, renderStep.ContributorId);
        this._renderSteps.Add(renderStep);
        this._renderStepIndex = null;
    }

    /// <summary>
    /// Gets the render steps associated with a contribution. Uses a pre-built index
    /// for O(1) lookup instead of repeated LINQ enumeration over the full step list.
    /// </summary>
    /// <param name="contribution">The contribution to look up.</param>
    /// <returns>The matching render steps in insertion order.</returns>
    public IReadOnlyList<PipelineRenderStep> GetRenderSteps(PipelineContribution contribution)
    {
        if (this._renderStepIndex is null)
        {
            this._renderStepIndex = new Dictionary<PipelineContribution, List<PipelineRenderStep>>(this._contributions.Count);

            foreach (var step in this._renderSteps)
            {
                var key = new PipelineContribution(step.Stage, step.ContributorId);

                if (!this._renderStepIndex.TryGetValue(key, out var list))
                {
                    list = new List<PipelineRenderStep>(4);
                    this._renderStepIndex[key] = list;
                }

                list.Add(step);
            }
        }

        return this._renderStepIndex.TryGetValue(contribution, out var steps)
            ? steps
            : [];
    }

    /// <summary>
    /// Registers a deferred class member that will be applied to the pipeline class builder
    /// before the generated source is built. Use this to add helper methods that sit alongside
    /// <c>ExecuteAsync</c> — methods cannot be added during render-step execution because the
    /// class builder has already materialised its member list at that point.
    /// </summary>
    /// <param name="configure">A callback that receives the pipeline <see cref="ClassBuilder"/> and adds the member.</param>
    public void AddDeferredClassMember(Action<ClassBuilder> configure)
    {
        this._deferredClassMembers.Add(configure);
    }

    /// <summary>
    /// Gets the deferred class member registrations.
    /// </summary>
    public IReadOnlyList<Action<ClassBuilder>> DeferredClassMembers => this._deferredClassMembers;
}

/// <summary>
/// Rendering context for a single pipeline render step.
/// </summary>
public sealed class PipelineRenderContext
{
    private readonly ICodeWriter _writer;
    private readonly OperationHandlerModel _handler;
    private readonly PipelinePlan _plan;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineRenderContext"/> class.
    /// </summary>
    /// <param name="writer">The writer receiving generated source.</param>
    /// <param name="handler">The resolved handler metadata for the operation.</param>
    /// <param name="plan">The pipeline plan being rendered.</param>
    public PipelineRenderContext(
        ICodeWriter writer,
        OperationHandlerModel handler,
        PipelinePlan plan)
    {
        this._writer = writer;
        this._handler = handler;
        this._plan = plan;
    }

    /// <summary>
    /// Gets the handler parameters.
    /// </summary>
    /// <returns>The handler parameters.</returns>
    public IEnumerable<HandlerParameter> GetHandlerParameters()
    {
        return this._handler.Parameters;
    }

    /// <summary>
    /// Gets a value indicating whether the handler is inline.
    /// </summary>
    /// <returns><c>true</c> if the handler is inline; otherwise <c>false</c>.</returns>
    public bool IsInlineHandler()
    {
        return this._handler.IsInline;
    }

    /// <summary>
    /// Gets a value indicating whether the handler method is static.
    /// </summary>
    /// <returns><c>true</c> if the handler method is static; otherwise <c>false</c>.</returns>
    public bool IsStaticHandler()
    {
        return this._handler.IsHandlerStatic;
    }

    /// <summary>
    /// Gets a value indicating whether the handler method is asynchronous.
    /// </summary>
    /// <returns><c>true</c> if the handler method is asynchronous; otherwise <c>false</c>.</returns>
    public bool IsHandlerAsync()
    {
        return this._handler.IsHandlerAsync;
    }

    /// <summary>
    /// Gets a value indicating whether the generated pipeline method is async, either because the handler itself
    /// is async or because a contributor (such as body binding) introduced an await expression.
    /// </summary>
    /// <returns><c>true</c> if the pipeline method will be emitted with the <c>async</c> modifier; otherwise <c>false</c>.</returns>
    public bool IsPipelineAsync()
    {
        return this._handler.IsHandlerAsync || this._plan.RequiresAsync;
    }

    /// <summary>
    /// Gets the fully qualified operation type name used by the handler.
    /// </summary>
    /// <returns>The operation type name.</returns>
    public string GetOperationTypeFullName()
    {
        return this._handler.OperationTypeFullName;
    }

    /// <summary>
    /// Gets the handler method name.
    /// </summary>
    /// <returns>The handler method name.</returns>
    public string GetHandlerMethodName()
    {
        return this._handler.HandlerMethodName;
    }

    /// <summary>
    /// Gets the handler class name or throws when the handler is inline.
    /// </summary>
    /// <returns>The handler class name.</returns>
    public string GetRequiredHandlerClassName()
    {
        return this._handler.HandlerClassName ?? throw new InvalidOperationException("The render step requires an external handler class name, but the current handler is inline.");
    }

    /// <summary>
    /// Gets the fully qualified handler class name or throws when the handler is inline.
    /// </summary>
    /// <returns>The fully qualified handler class name.</returns>
    public string GetRequiredHandlerClassFullName()
    {
        return this._handler.HandlerClassFullName ?? throw new InvalidOperationException("The render step requires an external handler class name, but the current handler is inline.");
    }

    /// <summary>
    /// Gets the property injection list for the current external handler, or <c>null</c> when
    /// the handler has no well-known settable base-class properties. Each entry contains
    /// the property name and the pipeline expression to assign.
    /// </summary>
    /// <returns>The property injections or <c>null</c>.</returns>
    public EquatableList<HandlerPropertyInjection>? GetPropertyInjections()
    {
        return this._handler.PropertyInjections;
    }

    /// <summary>
    /// Gets the in-scope API operation context expression.
    /// </summary>
    /// <returns>The context expression.</returns>
    public string GetContextExpression()
    {
        return "context";
    }

    /// <summary>
    /// Gets the in-scope operation expression.
    /// </summary>
    /// <returns>The operation expression.</returns>
    public string GetOperationExpression()
    {
        return "operation";
    }

    /// <summary>
    /// Gets an expression for a property on the API operation context.
    /// </summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property access expression.</returns>
    public string GetContextPropertyExpression(string propertyName)
    {
        return $"{this.GetContextExpression()}.{propertyName}";
    }

    /// <summary>
    /// Gets the expression for the current HTTP context. On first call this emits a
    /// <c>var httpContext = ...</c> declaration; subsequent calls (even from different modules)
    /// return the cached variable name without re-declaring.
    /// </summary>
    /// <returns>The <c>httpContext</c> variable expression.</returns>
    public string GetHttpContextExpression()
    {
        if (this._plan.TryDeclareSharedVariable("httpContext"))
        {
            this.DeclareVariable("httpContext", "((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext");
        }

        return "httpContext";
    }

    /// <summary>
    /// Gets the service provider expression.
    /// </summary>
    /// <returns>The service provider expression.</returns>
    public string GetServiceProviderExpression()
    {
        return $"{this.GetContextExpression()}.ServiceProvider";
    }

    /// <summary>
    /// Tries to find a constructor-cached field whose service type matches <paramref name="serviceTypeName"/>.
    /// Returns the <c>this._fieldName</c> expression when found, so callers can use the cached
    /// field instead of a per-request <c>GetRequiredService</c> call.
    /// </summary>
    /// <param name="serviceTypeName">The fully qualified service type to look up.</param>
    /// <param name="fieldExpression">When found, the <c>this._fieldName</c> expression; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if a matching cached service exists.</returns>
    public bool TryGetCachedServiceFieldExpression(string serviceTypeName, [NotNullWhen(true)] out string? fieldExpression)
    {
        return this._plan.TryGetCachedServiceFieldExpression(serviceTypeName, out fieldExpression);
    }

    /// <summary>
    /// Gets an expression that resolves a required service from the service provider.
    /// </summary>
    /// <param name="serviceTypeName">The service type name.</param>
    /// <returns>The service resolution expression.</returns>
    public string GetRequiredServiceExpression(string serviceTypeName)
    {
        return $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{serviceTypeName}>({this.GetServiceProviderExpression()})";
    }

    /// <summary>
    /// Gets an expression that resolves an optional service from the service provider.
    /// </summary>
    /// <param name="serviceTypeName">The service type name.</param>
    /// <returns>The service resolution expression.</returns>
    public string GetOptionalServiceExpression(string serviceTypeName)
    {
        return $"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<{serviceTypeName}>({this.GetServiceProviderExpression()})";
    }

    /// <summary>
    /// Gets an expression that constructs a service using activator utilities.
    /// </summary>
    /// <param name="serviceTypeName">The service type name.</param>
    /// <returns>The activator expression.</returns>
    public string GetActivatorCreateInstanceExpression(string serviceTypeName)
    {
        return $"Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<{serviceTypeName}>({this.GetServiceProviderExpression()})";
    }

    /// <summary>
    /// Gets an expression that constructs the external handler by calling its constructor directly
    /// with <c>GetRequiredService</c> for each parameter, avoiding the reflection overhead of
    /// <c>ActivatorUtilities.CreateInstance</c>. Falls back to <see cref="GetActivatorCreateInstanceExpression"/>
    /// when the constructor cannot be statically resolved. Constructor-cached singletons (e.g.
    /// <c>ILogger&lt;T&gt;</c>) use the pre-resolved field instead of per-request DI lookups.
    /// </summary>
    /// <param name="handlerTypeFullName">The fully qualified handler type name.</param>
    /// <returns>A <c>new Handler(...)</c> expression or an <c>ActivatorUtilities</c> fallback.</returns>
    public string GetCompiledHandlerConstructorExpression(string handlerTypeFullName)
    {
        var ctorParams = this._handler.ConstructorParameters;

        if (ctorParams is not { } resolved)
        {
            return this.GetActivatorCreateInstanceExpression(handlerTypeFullName);
        }

        if (resolved.Count == 0)
        {
            return $"new {handlerTypeFullName}()";
        }

        var sp = this.GetServiceProviderExpression();
        var argParts = new List<string>(resolved.Count);
        foreach (var p in resolved)
        {
            // Use constructor-cached field for known singletons (e.g. ILogger<T>)
            if (this._plan.TryGetCachedServiceFieldExpression(p.ParameterTypeName, out var cachedExpr))
            {
                argParts.Add(cachedExpr);
            }
            else
            {
                argParts.Add($"Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{p.ParameterTypeName}>({sp})");
            }
        }

        return $"new {handlerTypeFullName}({string.Join(", ", argParts)})";
    }

    /// <summary>
    /// Declares a variable in the generated code.
    /// </summary>
    /// <param name="variableName">The variable name.</param>
    /// <param name="expression">The initializing expression.</param>
    public void DeclareVariable(string variableName, string expression)
    {
        this._writer.DeclareVar(variableName, expression);
    }

    /// <summary>
    /// Assigns a value to an existing variable.
    /// </summary>
    /// <param name="variableName">The variable name.</param>
    /// <param name="expression">The assigned expression.</param>
    public void Assign(string variableName, string expression)
    {
        this._writer.Assign(variableName, expression);
    }

    /// <summary>
    /// Writes a blank line.
    /// </summary>
    public void WriteBlankLine()
    {
        this._writer.NewLine();
    }

    /// <summary>
    /// Emits a throw statement.
    /// </summary>
    /// <param name="expression">The exception expression to throw.</param>
    public void Throw(string expression)
    {
        this._writer.AppendLine($"throw {expression};");
    }

    /// <summary>
    /// Emits a single statement line.
    /// </summary>
    /// <param name="statement">The statement text.</param>
    public void WriteStatement(string statement)
    {
        this._writer.AppendLine(statement);
    }

    /// <summary>
    /// Emits a return statement.
    /// </summary>
    /// <param name="expression">The returned expression.</param>
    public void Return(string expression)
    {
        this._writer.Return(expression);
    }

    /// <summary>
    /// Emits a conditional block.
    /// </summary>
    /// <param name="condition">The conditional expression.</param>
    /// <param name="render">The nested render callback.</param>
    public void If(string condition, Action<PipelineRenderContext> render)
    {
        this._writer.If(condition, branchWriter => render(new PipelineRenderContext(branchWriter, this._handler, this._plan)));
    }

    /// <summary>
    /// Emits a <c>try</c> block. Must be followed by a <see cref="Catch"/> call.
    /// </summary>
    /// <param name="render">The nested render callback for the try body.</param>
    public void Try(Action<PipelineRenderContext> render)
    {
        this._writer.Try(tryWriter => render(new PipelineRenderContext(tryWriter, this._handler, this._plan)));
    }

    /// <summary>
    /// Emits a <c>catch</c> block following a <see cref="Try"/> call.
    /// </summary>
    /// <param name="exceptionDeclaration">The exception declaration (e.g. <c>"System.Exception ex"</c>).</param>
    /// <param name="render">The nested render callback for the catch body.</param>
    public void Catch(string exceptionDeclaration, Action<PipelineRenderContext> render)
    {
        this._writer.Catch(exceptionDeclaration, catchWriter => render(new PipelineRenderContext(catchWriter, this._handler, this._plan)));
    }

    /// <summary>
    /// Emits a <c>foreach</c> loop block.
    /// </summary>
    /// <param name="variableDeclaration">The loop variable declaration (e.g. <c>"var item"</c>).</param>
    /// <param name="collection">The collection expression to iterate.</param>
    /// <param name="render">The nested render callback for the loop body.</param>
    public void ForEach(string variableDeclaration, string collection, Action<PipelineRenderContext> render)
    {
        this._writer.Block($"foreach ({variableDeclaration} in {collection})", bodyWriter => render(new PipelineRenderContext(bodyWriter, this._handler, this._plan)));
    }

    /// <summary>
    /// Emits an <c>else</c> block following an <see cref="If"/> call.
    /// </summary>
    /// <param name="render">The nested render callback for the else body.</param>
    public void Else(Action<PipelineRenderContext> render)
    {
        this._writer.Else(elseWriter => render(new PipelineRenderContext(elseWriter, this._handler, this._plan)));
    }

    /// <summary>
    /// Emits an arbitrary block expression (e.g. a <c>for</c> loop or <c>lock</c> statement).
    /// </summary>
    /// <param name="expression">The block header expression (e.g. <c>"for (var i = 0; i &lt; 10; i++)"</c>).</param>
    /// <param name="render">The nested render callback for the block body.</param>
    public void Block(string expression, Action<PipelineRenderContext> render)
    {
        this._writer.Block(expression, bodyWriter => render(new PipelineRenderContext(bodyWriter, this._handler, this._plan)));
    }
}

/// <summary>
/// A single ordered pipeline contribution.
/// </summary>
/// <param name="Stage">The target stage.</param>
/// <param name="ContributorId">The contributing contributor identifier.</param>
public sealed record PipelineContribution(
    PipelineStage Stage,
    string ContributorId);

/// <summary>
/// A single ordered pipeline render step. Stored inline in lists to avoid per-step
/// heap allocation (8-15 steps per pipeline × 500 operations = thousands of allocations saved).
/// </summary>
/// <param name="stage">The target stage.</param>
/// <param name="contributorId">The contributing contributor identifier.</param>
/// <param name="render">The contributor-owned rendering callback.</param>
/// <param name="requiresSuccessGuard">
/// When <c>true</c>, the code generator wraps this step (and adjacent success-guarded steps)
/// inside a single <c>if (handlerResult.WasSuccessful)</c> block instead of each module
/// emitting its own guard. This eliminates repeated virtual-dispatch checks in the generated
/// pipeline and produces a single consolidated PostExecution success block.
/// </param>
public readonly struct PipelineRenderStep(
    PipelineStage stage,
    string contributorId,
    Action<PipelineRenderContext> render,
    bool requiresSuccessGuard = false)
{
    private readonly Action<PipelineRenderContext> _render = render ?? throw new ArgumentNullException(nameof(render));

    /// <summary>
    /// Gets the target stage.
    /// </summary>
    public PipelineStage Stage { get; } = stage;

    /// <summary>
    /// Gets the contributing contributor identifier.
    /// </summary>
    public string ContributorId { get; } = contributorId;

    /// <summary>
    /// Gets a value indicating whether this step requires the outer <c>handlerResult.WasSuccessful</c> guard.
    /// </summary>
    public bool RequiresSuccessGuard { get; } = requiresSuccessGuard;

    /// <summary>
    /// Invokes the contributor-owned render callback.
    /// </summary>
    /// <param name="context">The render context.</param>
    public void Render(PipelineRenderContext context)
    {
        this._render(context);
    }
}

/// <summary>
/// Describes a service that should be resolved from the root <c>IServiceProvider</c> at pipeline
/// construction time and cached as a private readonly field for zero-cost per-request access.
/// </summary>
/// <param name="FieldName">The private field name on the generated pipeline class.</param>
/// <param name="ServiceTypeName">The fully-qualified CLR type for the field declaration.</param>
/// <param name="ResolutionExpression">
/// The C# expression to resolve the value in the constructor (may reference <c>serviceProvider</c>).
/// </param>
/// <param name="IsNullable">Whether the resolved value may be null.</param>
public sealed record CachedServiceRegistration(
    string FieldName,
    string ServiceTypeName,
    string ResolutionExpression,
    bool IsNullable);
