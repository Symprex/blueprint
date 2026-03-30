using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects a compile-time inventory of body payload types from HTTP response metadata so later serializer-context and
/// schema slices can reason from descriptor facts instead of rediscovering response shapes from runtime behavior.
/// </summary>
public sealed class SerializerContextCoverageModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(SerializerContextCoverageModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(SerializerContextCoverageModule);

    /// <inheritdoc />
    public int Order => 80;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } =
    [
        nameof(HttpResponseMetadataModule),
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var coverage = context.GetMetadata<HttpResponseMetadataRegistration>()
            .SelectMany(static response => response.BodyTypeNames)
            .Distinct(StringComparer.Ordinal)
            .Select(static bodyTypeName => new SerializerContextCoverageRegistration(bodyTypeName))
            .ToArray();
        if (coverage.Length == 0)
        {
            return;
        }

        foreach (var registration in coverage)
        {
            context.AddMetadata(registration);
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<SerializerContextCoverageRegistration>()));
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<SerializerContextCoverageRegistration> coverage)
    {
        var bodyTypeNames = coverage
            .Select(static registration => registration.BodyTypeName)
            .Distinct(StringComparer.Ordinal)
            .Select(static bodyTypeName => CSharpSymbolDisplay.FormatLiteral(bodyTypeName, quote: true))
            .ToArray();
        if (bodyTypeNames.Length == 0)
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationSerializerContextCoverageFeatureData("
            + $"global::System.Collections.Immutable.ImmutableArray.Create<string>({string.Join(", ", bodyTypeNames)})));");
    }
}
