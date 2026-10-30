using System.Text;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the pre-rendered document is held as UTF-8, which is also how it is served, while <c>Json</c> still reads as
/// the string it always was.
/// </summary>
public sealed class PreRenderedOpenApiDocumentTests
{
    // Two-, three- and four-byte sequences, whose UTF-16 and UTF-8 lengths differ.
    private const string Text = "{\"d\":\"café € \U0001F600\"}";

    [Fact]
    public void When_made_from_a_string_then_it_is_held_as_utf8_and_reads_back_as_the_string()
    {
        var document = new PreRenderedOpenApiDocument(Text);

        // Taken from the Unicode charts: e-acute is C3 A9, the euro sign E2 82 AC and the grinning face F0 9F 98 80.
        Convert.ToHexString(document.Utf8Json.Span).Should().Be("7B2264223A22" + "636166" + "C3A9" + "20" + "E282AC" + "20" + "F09F9880" + "227D");
        document.Json.Should().Be(Text);
    }

    [Fact]
    public void When_made_from_utf8_then_the_json_is_decoded_from_it()
    {
        var document = new PreRenderedOpenApiDocument(Encoding.UTF8.GetBytes(Text));

        document.Json.Should().Be(Text);
    }

    [Fact]
    public async Task When_served_then_the_response_is_the_documents_utf8()
    {
        var (handler, document) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { Response = { Body = body } };
        httpContext.Request.Method = HttpMethods.Get;

        await handler(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        httpContext.Response.ContentType.Should().Be("application/json; charset=utf-8");
        body.ToArray().Should().Equal(document.Utf8Json.ToArray());
        document.Json.Should().Contain("\"openapi\"");
    }
}
