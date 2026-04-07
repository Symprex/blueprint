using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Authentication;

/// <summary>
/// Detects <c>[MustBeAuthenticated]</c> on operations and emits both descriptor feature data
/// and a pipeline identity check in the <see cref="PipelineStage.Authentication"/> stage.
/// Unauthenticated HTTP requests are short-circuited with a <c>401 Unauthorized</c> response.
/// </summary>
public sealed class AuthenticationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IPipelineContributor
{
    private const string MustBeAuthenticatedAttributeName = "MustBeAuthenticatedAttribute";
    private const string AttributeNamespace = "Blueprint.Model.Attributes";

    /// <inheritdoc />
    public string ModuleId => nameof(AuthenticationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AuthenticationModule);

    /// <inheritdoc />
    /// <remarks>
    /// Must run after <c>LinkGenerationModule</c> and <c>TasksHostModule</c> (both <c>Order = 0</c>)
    /// so that <see cref="HostExposureRegistration"/> metadata is available when checking whether
    /// the operation is HTTP-exposed.
    /// </remarks>
    public int Order => 50;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        if (!HasMustBeAuthenticated(operationType))
        {
            return;
        }

        var isHttpExposed = context.GetMetadata<HostExposureRegistration>()
            .Any(static r => string.Equals(r.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));

        if (!isHttpExposed)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    AuthenticationDiagnosticDescriptors.AuthAttributeOnNonHttpOperation,
                    operationType.Locations.FirstOrDefault(),
                    [operationType.Name]));

            return;
        }

        context.AddMetadata(new AuthenticationDescriptorRegistration());
        context.AddRenderStep(RenderDescriptorFeatureData);
    }

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;

        if (!HasMustBeAuthenticated(operationType))
        {
            return;
        }

        if (!HasHttpHostExposure(operationType))
        {
            return;
        }

        context.AddRenderStep(
            PipelineStage.Authentication,
            RenderAuthenticationCheck);
    }

    private static bool HasMustBeAuthenticated(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes().Any(static a =>
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
            {
                return false;
            }

            return attrClass.Name == MustBeAuthenticatedAttributeName
                   && string.Equals(
                       attrClass.ContainingNamespace?.ToDisplayString(),
                       AttributeNamespace,
                       StringComparison.Ordinal);
        });
    }

    private static bool HasHttpHostExposure(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Any(static a => a.AttributeClass?.Name is "RootLinkAttribute" or "SelfLinkAttribute");
    }

    private static void RenderDescriptorFeatureData(CodeWriter.ICodeWriter writer)
    {
        writer.AppendLine(
            "this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.RequiresAuthentication);");
    }

    private static void RenderAuthenticationCheck(PipelineRenderContext context)
    {
        context.If(
            "((Blueprint.Runtime.Http.IHttpOperationContext)context).HttpContext.User.Identity?.IsAuthenticated != true",
            branch =>
            {
                branch.Return(
                    context.IsHandlerAsync()
                        ? "new Blueprint.Runtime.Exec.UnauthenticatedResult()"
                        : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.UnauthenticatedResult())");
            });

        context.WriteBlankLine();
    }
}
