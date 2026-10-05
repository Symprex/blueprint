using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a property typed as the interface <c>IEnumerable&lt;T&gt;</c> itself, not a type that implements it, is
/// documented as an array of <c>T</c>, as a <c>List&lt;T&gt;</c> or an array of the same is.
/// </summary>
/// <remarks>
/// <c>typeof(IEnumerable&lt;T&gt;).GetInterfaces()</c> does not include the interface itself, so a request body property of
/// that type (Signature365's <c>SignatureRuleCommand.ApplyTo</c>, <c>ApplyToDomains</c> and <c>AdditionalSignatures</c>) was
/// documented as a <c>string</c>, and the generated client typed it as one. A query parameter cannot be one: the source
/// generator refuses to bind a query string to anything but an array of scalars.
/// </remarks>
public sealed class OpenApiEnumerableInterfaceTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_request_body_property_typed_as_IEnumerable_is_an_array_of_its_element_type(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var numbers = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SubmitEnumerableInterface")
            .GetProperty("properties").EnumerateObject()
            .Single(static property => string.Equals(property.Name, "numbers", StringComparison.OrdinalIgnoreCase))
            .Value;

        numbers.GetProperty("type").GetString().Should().Be("array");
        numbers.GetProperty("items").GetProperty("type").GetString().Should().Be("integer");
        numbers.GetProperty("items").GetProperty("format").GetString().Should().Be("int32");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>Submits a body property typed as the <c>IEnumerable&lt;int&gt;</c> interface.</summary>
[AllowAnonymous]
[RootLink("/enumerable-interface")]
[HttpPost]
public partial class SubmitEnumerableInterfaceCommand : ICommand<string>
{
    public IEnumerable<int>? Numbers { get; set; }

    public static string Handle(SubmitEnumerableInterfaceCommand command)
    {
        return string.Join(',', command.Numbers ?? []);
    }
}
