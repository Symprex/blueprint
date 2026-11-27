using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes a render step at the end of the Validation stage that calls <c>IUnitOfWork.BeginAsync</c> before the
/// handler, and a PostExecution render step that calls <c>IUnitOfWork.SaveChangesAsync</c> after successful handler
/// execution. Participates for every operation without <c>[DoNotSaveChanges]</c> — commands,
/// background tasks and queries alike — and skips gracefully at runtime if no <c>IUnitOfWork</c> is registered in DI.
/// </summary>
/// <remarks>
/// <para>
/// This is legacy's rule: Signature365's <c>SaveMartenChangesMiddleware</c> matched any operation whose type had no
/// <c>[DoNotSaveChanges]</c> (Signature365 <c>8b40ecc961</c>, <c>Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29</c>),
/// so a query's pipeline saved its session too (Portal <c>Microsoft365AddOnConnectQueryExecutorPipeline.cs:133-134</c>).
/// Legacy left the save out of a nested execution (<c>SupportsNestedExecution =&gt; false</c>), which this framework
/// makes only for a resource event's self query, sharing its command's session. So a query's save, and that of any
/// operation that is neither a command nor a background task (<see cref="IsSkippedWhenNested"/>), is skipped when the
/// context is nested. A command's and a background task's save runs nested or not, as it always has here.
/// </para>
/// <para>
/// The begin step follows the save's rule, nested skip included. It is rendered once the operation is authorised and
/// validated, so a refused or invalid request begins nothing, and before the Rebus transaction scope opens (a Consumer
/// module, so later in the stage), so a unit of work enlisted in that scope has already begun. It lets a unit of work over
/// a relational session open its transaction before the handler writes, as Timestamp's legacy
/// <c>NHibernateBeforeMiddleware</c> did; without it, writes the session issues before the save commit on their own and
/// survive a handler that then throws.
/// </para>
/// <para>
/// Disposal of the unit-of-work instance is owned by the DI scope, not by this module.
/// Implementations should register as scoped services so the container manages their lifetime.
/// </para>
/// <para>
/// This module orders before <see cref="BackgroundTaskFlushModule"/> to ensure that persistence
/// is committed before background tasks are dispatched, preserving read-your-writes semantics
/// for any tasks that reference the committed data.
/// </para>
/// </remarks>
public sealed class UnitOfWorkModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <summary>
    /// The most reruns <c>[RetryOnConflict]</c> may ask for: <c>Blueprint.Persistence.RetryOnConflictAttribute.MaxRetries</c>,
    /// which the generator, a netstandard2.0 assembly that does not reference the runtime, cannot read.
    /// </summary>
    private const int MaxRefusedSaveRetries = 5;

    /// <inheritdoc />
    public string ModuleId => nameof(UnitOfWorkModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(UnitOfWorkModule);

    /// <inheritdoc />
    public int Order => 900;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        // Checked whether or not this pipeline saves: a resource event's self query saves before it runs, even under
        // [DoNotSaveChanges], and answers a conflict with the same factory.
        var concurrencyConflictFactory = ResolveConcurrencyConflictFactory(context, operationType);
        var retries = ResolveRefusedSaveRetries(context, operationType);

        if (!SavesUnitOfWork(operationType))
        {
            return;
        }

        if (retries > 0)
        {
            context.RetryRefusedSave(retries);
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        var isSkippedWhenNested = IsSkippedWhenNested(operationType);
        var saveArguments = concurrencyConflictFactory is null
            ? "context.ServiceProvider, context.OperationCancelled"
            : "context.ServiceProvider, context.OperationCancelled, " + concurrencyConflictFactory;

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.Validation, isSkippedWhenNested ? RenderBeginUnlessNested : RenderBegin);
        context.AddSuccessGuardedRenderStep(
            PipelineStage.PostExecution,
            ctx => RenderSaveChanges(ctx, isSkippedWhenNested ? "if (this.HasUnitOfWork && !context.IsNested)" : "if (this.HasUnitOfWork)", saveArguments));
    }

    /// <summary>
    /// How many times the operation's <c>[RetryOnConflict]</c> asks for its pipeline to be rerun after a refused save; zero
    /// when it has none, or when it cannot be honoured, which this module reports: BPP003 under <c>[DoNotSaveChanges]</c>,
    /// BPP004 under <c>[NoRebusTransaction]</c>, BPP005 for a count outside 1 to 5 (ADR 0001).
    /// </summary>
    private static int ResolveRefusedSaveRetries(PipelineContributorContext context, INamedTypeSymbol operationType)
    {
        var attribute = operationType.GetAttributes().FirstOrDefault(static attr =>
            attr.AttributeClass is { Name: "RetryOnConflictAttribute" } attributeClass
            && attributeClass.IsInNamespace("Blueprint", "Persistence"));

        if (attribute is null)
        {
            return 0;
        }

        var location = attribute.ApplicationSyntaxReference is { } reference
            ? LocationInfo.CreateFrom(reference.GetSyntax())
            : LocationInfo.CreateFrom(operationType.Locations.First());

        if (!SavesUnitOfWork(operationType))
        {
            context.AddDiagnostic(new DiagnosticInfo(PersistenceDiagnosticDescriptors.RetryOnConflictWithoutSave, location, [operationType.Name]));
            return 0;
        }

        if (RebusTransactionModule.HasNoRebusTransactionAttribute(operationType))
        {
            context.AddDiagnostic(new DiagnosticInfo(PersistenceDiagnosticDescriptors.RetryOnConflictWithoutRebusTransaction, location, [operationType.Name]));
            return 0;
        }

        // The constructor's default (1) is in ConstructorArguments when the attribute is written without one.
        var retries = attribute.ConstructorArguments is [{ Value: int count }] ? count : 1;

        if (retries is < 1 or > MaxRefusedSaveRetries)
        {
            context.AddDiagnostic(new DiagnosticInfo(
                PersistenceDiagnosticDescriptors.RetryOnConflictCountOutOfRange,
                location,
                [retries.ToString(System.Globalization.CultureInfo.InvariantCulture), operationType.Name]));
            return 0;
        }

        return retries;
    }

    /// <summary>
    /// The expression for the <c>ApiExceptionFactory</c> the operation's <c>[ConcurrencyConflictError]</c> names, which the save
    /// is passed so a concurrency conflict is answered with the operation's own 409; <see langword="null"/> when it names none,
    /// so the save answers with Blueprint's default, or names no static factory of its own, which is BPP002.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Like a <c>[TranslateError]</c> target, the member is looked for on the operation class only, and may be a field or
    /// a property, read through its getter each time a save meets a concurrency conflict.
    /// </para>
    /// <para>
    /// Unlike one, it is read by the generated pipeline, a class of its own, so it must be readable from there: never a
    /// private member or getter. An operation in this compilation shares the pipeline's assembly, so an internal member
    /// will do. One scanned from a referenced assembly has its pipeline compiled here, in another assembly, so its member,
    /// and every type containing it, must be public.
    /// </para>
    /// </remarks>
    private static string? ResolveConcurrencyConflictFactory(PipelineContributorContext context, INamedTypeSymbol operationType)
    {
        if (ApiExceptionFactoryResolver.GetConcurrencyConflictError(operationType).FirstOrDefault() is not { } factory)
        {
            return null;
        }

        var expression = GetConcurrencyConflictFactoryExpression(operationType);

        if (expression is null)
        {
            var attribute = operationType.GetAttributes().First(static attr => attr.AttributeClass?.Name is "ConcurrencyConflictErrorAttribute");
            var location = attribute.ApplicationSyntaxReference is { } reference
                ? LocationInfo.CreateFrom(reference.GetSyntax())
                : LocationInfo.CreateFrom(operationType.Locations.First());

            context.AddDiagnostic(new DiagnosticInfo(
                PersistenceDiagnosticDescriptors.ConcurrencyConflictErrorIsNotAFactory,
                location,
                [factory, operationType.Name]));

            return null;
        }

        return expression;
    }

    /// <summary>
    /// The expression for the factory the operation's <c>[ConcurrencyConflictError]</c> names, when it names one the generated
    /// pipeline can read (see <see cref="ResolveConcurrencyConflictFactory"/>); otherwise <see langword="null"/>, and this module
    /// reports why. Shared with the resource event self query, whose save before the query is answered the same way.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <returns>The expression, or <see langword="null"/>.</returns>
    internal static string? GetConcurrencyConflictFactoryExpression(INamedTypeSymbol operationType)
    {
        if (ApiExceptionFactoryResolver.GetConcurrencyConflictError(operationType).FirstOrDefault() is not { } factory)
        {
            return null;
        }

        var isFromSource = !operationType.DeclaringSyntaxReferences.IsEmpty;
        var isFactory = operationType.GetMembers(factory).Any(member => member.IsStatic
            && (isFromSource ? ApiExceptionFactoryResolver.CanRead(member, operationType.ContainingAssembly) : IsPublicAll(member))
            && member switch
            {
                IFieldSymbol field => IsApiExceptionFactory(field.Type),
                IPropertySymbol property => property.GetMethod is not null && IsApiExceptionFactory(property.Type),
                _ => false,
            });

        // `nameof(@event)` is "event", which must be written `@event` to compile.
        return isFactory
            ? operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + ValidationSymbolHelpers.EscapeIdentifier(factory)
            : null;
    }

    private static bool IsPublicAll(ISymbol member)
    {
        if (member is IPropertySymbol { GetMethod.DeclaredAccessibility: not Accessibility.Public })
        {
            return false;
        }

        for (var current = member; current is not null and not INamespaceSymbol; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsApiExceptionFactory(ITypeSymbol type)
    {
        // The metadata name carries no nullable annotation, which ToString() would ("ApiExceptionFactory?").
        return type is INamedTypeSymbol named
               && (named.ToFullMetadataName() == "Blueprint.ApiExceptionFactory" || named.InheritsFrom("Blueprint.ApiExceptionFactory"));
    }

    /// <summary>
    /// Whether the pipeline generated for <paramref name="operationType"/> saves the unit of work: any operation without
    /// <c>[DoNotSaveChanges]</c>, as legacy's <c>SaveMartenChangesMiddleware</c> matched (Signature365 <c>8b40ecc961</c>,
    /// <c>Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29</c>).
    /// </summary>
    /// <remarks>
    /// The one definition of that condition, shared with modules that must know whether a save will follow — the Rebus
    /// transaction enlists its outbox only where one does, and the debug manifest reports it.
    /// </remarks>
    /// <param name="operationType">The operation type.</param>
    /// <returns><see langword="true"/> when the generated pipeline contains the unit of work save.</returns>
    internal static bool SavesUnitOfWork(INamedTypeSymbol operationType)
    {
        return !HasDoNotSaveChangesAttribute(operationType);
    }

    /// <summary>
    /// Whether the unit of work save, and the Rebus transaction scope and enlistment, are left out of a nested execution of
    /// the pipeline generated for <paramref name="operationType"/>: any operation that is neither a command nor a
    /// background task.
    /// </summary>
    /// <remarks>
    /// Legacy left all of them out of every nested execution (<c>SupportsNestedExecution =&gt; false</c> on
    /// <c>SaveMartenChangesMiddleware</c>, <c>UseMartenAsOutboxMiddleware</c> and the Rebus scope middlewares;
    /// <c>Blueprint/CodeGennedExecutor.cs:99-100</c>). The one nested execution this framework makes is a resource event's
    /// self query, which shares its command's scope and session, so a query must not open a scope of its own, enlist the
    /// session again or save it there. Commands and background tasks took part before queries did, nested or not, and
    /// still do: their nested behaviour is not changed.
    /// </remarks>
    /// <param name="operationType">The operation type.</param>
    /// <returns><see langword="true"/> when the generated code checks <c>context.IsNested</c> first.</returns>
    internal static bool IsSkippedWhenNested(INamedTypeSymbol operationType)
    {
        return !IsCommand(operationType) && !IsBackgroundTask(operationType);
    }

    private static void RenderBegin(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("if (this.HasUnitOfWork)");
        context.WriteStatement("{");
        context.WriteStatement("    await BeginUnitOfWorkAsync(context).ConfigureAwait(false);");
        context.WriteStatement("}");
    }

    private static void RenderBeginUnlessNested(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.WriteStatement("if (this.HasUnitOfWork && !context.IsNested)");
        context.WriteStatement("{");
        context.WriteStatement("    await BeginUnitOfWorkAsync(context).ConfigureAwait(false);");
        context.WriteStatement("}");
    }

    private static void RenderSaveChanges(PipelineRenderContext context, string guard, string arguments)
    {
        context.WriteBlankLine();
        context.WriteStatement(guard);
        context.WriteStatement("{");
        context.WriteStatement("    await SaveUnitOfWorkAsync(" + arguments + ").ConfigureAwait(false);");
        context.WriteStatement("}");
    }

    private static bool IsCommand(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "ICommand"
            && candidate.IsInNamespace("Blueprint", "Model"));
    }

    private static bool IsBackgroundTask(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name is "IBackgroundTask"
            && candidate.IsInNamespace("Blueprint", "Tasks"));
    }

    private static bool HasDoNotSaveChangesAttribute(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "DoNotSaveChangesAttribute"
            && attr.AttributeClass.IsInNamespace("Blueprint", "Persistence"));
    }
}
