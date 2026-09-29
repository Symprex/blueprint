using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Pins the descriptions of <see cref="Blueprint.Model.Expandable{T}"/>'s component and properties to the texts of legacy
/// Blueprint's document (Signature365 8b40ecc961 committed Portal document, <c>ExpandableOfAzureAdGroup</c>), which
/// came from the XML docs of Signature365's own <c>Expandable&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// The expected texts are copied from that document, not derived from the framework's XML docs. The framework's summaries
/// are legacy's, in the same way <c>ApiResource.Object</c>'s is, so every naming writes them.
/// </remarks>
public class OpenApiExpandableDescriptionTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy, "ExpandableOfDepartmentResource")]
    [InlineData(OpenApiSchemaNaming.Default, "ExpandableOfDepartment")]
    public void Expandable_is_described_as_legacy_described_it(OpenApiSchemaNaming naming, string componentName)
    {
        var options = new OpenApiInfoOptions { SchemaNaming = naming };
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);
        using var document = JsonDocument.Parse(rendered.Json);

        var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(componentName);
        var properties = component.GetProperty("properties");

        component.GetProperty("description").GetString().Should().Be("Represents an expandable resource property.");
        properties.GetProperty("Id").GetProperty("description").GetString().Should().Be("The ID of this expandable value.");
        properties.GetProperty("Value").GetProperty("description").GetString()
            .Should().Be("The value itself, which will be null if the value has not been expanded.");
        properties.GetProperty("ExpandError").GetProperty("description").GetString()
            .Should().Be("When set, indicates that expansion was attempted but failed for the given reason.\nValue will be null.");
    }
}
