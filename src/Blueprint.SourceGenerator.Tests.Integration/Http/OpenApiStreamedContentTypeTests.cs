using System.Text;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// A streamed result's success response is documented with the media types its operation declares with
/// <see cref="ProducesContentTypeAttribute"/>, each a binary string (D18). Without the attribute, a
/// <see cref="FileStreamResult"/> stays <c>application/octet-stream</c> and a <see cref="ProxyResult"/> stays without content.
/// </summary>
public sealed class OpenApiStreamedContentTypeTests
{
    [Fact]
    public void A_file_result_without_a_declared_content_type_is_documented_as_octet_stream()
    {
        using var document = Render();

        var content = SuccessContent(document, "/streamed/file");

        content.EnumerateObject().Select(static media => media.Name).Should().Equal("application/octet-stream");
        AssertBinary(content.GetProperty("application/octet-stream"));
    }

    [Fact]
    public void A_file_result_is_documented_with_its_declared_content_types()
    {
        using var document = Render();

        var content = SuccessContent(document, "/streamed/vcard");

        content.EnumerateObject().Select(static media => media.Name).Should().Equal("text/vcard", "text/x-vcard");
        AssertBinary(content.GetProperty("text/vcard"));
        AssertBinary(content.GetProperty("text/x-vcard"));
    }

    [Fact]
    public void A_proxy_result_without_a_declared_content_type_has_no_documented_content()
    {
        using var document = Render();

        Response(document, "/streamed/proxy").TryGetProperty("content", out _).Should().BeFalse();
    }

    [Fact]
    public void A_proxy_result_is_documented_with_its_declared_content_type()
    {
        using var document = Render();

        var content = SuccessContent(document, "/streamed/proxy-pdf");

        content.EnumerateObject().Select(static media => media.Name).Should().Equal("application/pdf");
        AssertBinary(content.GetProperty("application/pdf"));
    }

    [Fact]
    public void A_derived_proxy_result_is_documented_with_its_declared_content_type()
    {
        // This used to be matched by exact type name, so a ProxyResult subclass lost its declared media type and its body.
        using var document = Render();

        var content = SuccessContent(document, "/streamed/derived-proxy-pdf");

        content.EnumerateObject().Select(static media => media.Name).Should().Equal("application/pdf");
        AssertBinary(content.GetProperty("application/pdf"));
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Response(JsonDocument document, string path)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("200");
    }

    private static JsonElement SuccessContent(JsonDocument document, string path)
    {
        return Response(document, path).GetProperty("content");
    }

    private static void AssertBinary(JsonElement media)
    {
        var schema = media.GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("format").GetString().Should().Be("binary");
    }
}

/// <summary>Downloads a file with no declared media type.</summary>
[AllowAnonymous]
[RootLink("/streamed/file")]
public partial class GetStreamedFileQuery : IQuery<FileStreamResult>
{
    public static FileStreamResult Handle(GetStreamedFileQuery query)
    {
        return new FileStreamResult(new MemoryStream(Encoding.UTF8.GetBytes("file")), "application/octet-stream");
    }
}

/// <summary>Downloads a vCard, declared as either of its media types.</summary>
[AllowAnonymous]
[RootLink("/streamed/vcard")]
[ProducesContentType("text/vcard", "text/x-vcard")]
public partial class GetStreamedVCardQuery : IQuery<FileStreamResult>
{
    public static FileStreamResult Handle(GetStreamedVCardQuery query)
    {
        return new FileStreamResult(new MemoryStream(Encoding.UTF8.GetBytes("BEGIN:VCARD")), "text/vcard");
    }
}

/// <summary>Proxies an upstream response with no declared media type.</summary>
[AllowAnonymous]
[RootLink("/streamed/proxy")]
public partial class GetStreamedProxyQuery : IQuery<ProxyResult>
{
    public static ProxyResult Handle(GetStreamedProxyQuery query)
    {
        return new ProxyResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

/// <summary>Proxies an upstream PDF.</summary>
[AllowAnonymous]
[RootLink("/streamed/proxy-pdf")]
[ProducesContentType("application/pdf")]
public partial class GetStreamedProxyPdfQuery : IQuery<ProxyResult>
{
    public static ProxyResult Handle(GetStreamedProxyPdfQuery query)
    {
        return new ProxyResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

/// <summary>A proxy result of the integration app's own, as a gateway might add to label its upstream.</summary>
public sealed class DerivedPdfProxyResult : ProxyResult
{
    public DerivedPdfProxyResult(HttpResponseMessage downstreamResponse)
        : base(downstreamResponse)
    {
    }
}

/// <summary>Proxies an upstream PDF as a <see cref="DerivedPdfProxyResult"/>.</summary>
[AllowAnonymous]
[RootLink("/streamed/derived-proxy-pdf")]
[ProducesContentType("application/pdf")]
public partial class GetStreamedDerivedProxyPdfQuery : IQuery<DerivedPdfProxyResult>
{
    public static DerivedPdfProxyResult Handle(GetStreamedDerivedProxyPdfQuery query)
    {
        return new DerivedPdfProxyResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
