using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Contributes per-operation authorization metadata to the shared debug manifest, enabling
/// build-time <c>operations.auth</c> style inspection of access mode, scope requirements,
/// and inheritance provenance without runtime overhead.
/// </summary>
/// <remarks>
/// This module must run after <see cref="AuthorizationModule"/> (which projects
/// <see cref="AuthorizationDescriptorRegistration"/> and <see cref="AuthenticationDescriptorRegistration"/>
/// into the descriptor plan) so it can read those metadata entries rather than re-deriving them.
/// </remarks>
internal sealed class AuthorizationManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    private const string ResourceAttributeName = "ResourceAttribute";
    private const string FromResourceAttributeName = "FromResourceAttribute";
    private const string AllowAnonymousAttributeName = "AllowAnonymousAttribute";
    private const string MustBeAuthenticatedAttributeName = "MustBeAuthenticatedAttribute";
    private const string AttributeNamespace = "Blueprint.Model.Attributes";

    /// <inheritdoc />
    public string ModuleId => nameof(AuthorizationManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AuthorizationManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        var accessMode = DetermineAccessMode(operationType, context);
        context.AddEntry("auth.accessMode", accessMode);

        if (accessMode == "anonymous")
        {
            return;
        }

        if (accessMode == "permissioned")
        {
            var scopePairs = ResolveScopePairs(operationType);
            var source = DetermineAuthorizationSource(operationType);

            context.AddEntry("auth.scopePairs", RenderScopePairsArray(scopePairs));
            context.AddEntry("auth.source", source);
        }
    }

    private static string DetermineAccessMode(
        INamedTypeSymbol operationType,
        DebugManifestContributorContext context)
    {
        if (HasAttribute(operationType, AllowAnonymousAttributeName))
        {
            return "anonymous";
        }

        // Permissioned operations have authorization registration
        var hasAuthorization = context.GetMetadata<AuthorizationDescriptorRegistration>()
            .Any(static r => r.RequiresPermissionCheck);

        if (hasAuthorization)
        {
            return "permissioned";
        }

        // Authenticated-only operations (no resource scope pairs)
        var hasAuthentication = context.GetMetadata<AuthenticationDescriptorRegistration>()
            .Any(static r => r.RequiresIdentityCheck);

        if (hasAuthentication)
        {
            return "authenticated";
        }

        return "default";
    }

    private static List<(string Noun, string Verb)> ResolveScopePairs(INamedTypeSymbol operationType)
    {
        // Check for [FromResource] first (it takes precedence in scope resolution)
        var fromResourceTarget = GetFromResourceTarget(operationType);

        if (fromResourceTarget is not null)
        {
            return GetResourceAttributes(fromResourceTarget);
        }

        return GetResourceAttributes(operationType);
    }

    private static string DetermineAuthorizationSource(INamedTypeSymbol operationType)
    {
        var fromResourceTarget = GetFromResourceTarget(operationType);

        if (fromResourceTarget is not null)
        {
            return $"inherited:{fromResourceTarget.ToDisplayString()}";
        }

        return "direct";
    }

    private static JArray RenderScopePairsArray(List<(string Noun, string Verb)> scopePairs)
    {
        var array = new JArray();

        foreach (var (noun, verb) in scopePairs)
        {
            array.Add(new JObject
            {
                ["noun"] = noun,
                ["verb"] = verb,
            });
        }

        return array;
    }

    private static INamedTypeSymbol? GetFromResourceTarget(INamedTypeSymbol operationType)
    {
        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null)
            {
                continue;
            }

            if (attrClass.Name != FromResourceAttributeName ||
                !string.Equals(attrClass.ContainingNamespace?.ToDisplayString(), AttributeNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 1)
            {
                continue;
            }

            var typeArg = attribute.ConstructorArguments[0];

            if (typeArg.Kind != TypedConstantKind.Type || typeArg.Value is not INamedTypeSymbol targetType)
            {
                continue;
            }

            if (targetType.TypeKind == TypeKind.Error)
            {
                continue;
            }

            return targetType;
        }

        return null;
    }

    private static List<(string Noun, string Verb)> GetResourceAttributes(INamedTypeSymbol operationType)
    {
        var result = new List<(string Noun, string Verb)>();

        foreach (var attribute in operationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;

            if (attrClass is null)
            {
                continue;
            }

            if (attrClass.Name != ResourceAttributeName ||
                !string.Equals(attrClass.ContainingNamespace?.ToDisplayString(), AttributeNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 2)
            {
                continue;
            }

            var noun = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
            var verb = attribute.ConstructorArguments[1].Value as string ?? string.Empty;

            result.Add((noun, verb));
        }

        return result;
    }

    private static bool HasAttribute(INamedTypeSymbol operationType, string attributeName)
    {
        return operationType.GetAttributes().Any(a =>
        {
            var attrClass = a.AttributeClass;

            if (attrClass is null)
            {
                return false;
            }

            return attrClass.Name == attributeName
                   && string.Equals(
                       attrClass.ContainingNamespace?.ToDisplayString(),
                       AttributeNamespace,
                       StringComparison.Ordinal);
        });
    }
}
