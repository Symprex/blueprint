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
    private readonly List<DiagnosticInfo> _diagnostics = [];
    private readonly HashSet<PipelineContribution> _contributionSet = [];
    private readonly List<PipelineContribution> _contributions = [];
    private readonly List<PipelineRenderStep> _renderSteps = [];
    private readonly List<Action<ClassBuilder>> _deferredClassMembers = [];
    private readonly HashSet<string> _declaredSharedVariables = [];

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
    }

    /// <summary>
    /// Gets the render steps associated with a contribution.
    /// </summary>
    /// <param name="contribution">The contribution to look up.</param>
    /// <returns>The matching render steps in insertion order.</returns>
    public IEnumerable<PipelineRenderStep> GetRenderSteps(PipelineContribution contribution)
    {
        return this._renderSteps.Where(step => step.Stage == contribution.Stage && step.ContributorId == contribution.ContributorId);
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
/// A single ordered pipeline render step.
/// </summary>
/// <param name="stage">The target stage.</param>
/// <param name="contributorId">The contributing contributor identifier.</param>
/// <param name="render">The contributor-owned rendering callback.</param>
public sealed class PipelineRenderStep(
    PipelineStage stage,
    string contributorId,
    Action<PipelineRenderContext> render)
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
    /// Invokes the contributor-owned render callback.
    /// </summary>
    /// <param name="context">The render context.</param>
    public void Render(PipelineRenderContext context)
    {
        this._render(context);
    }
}
