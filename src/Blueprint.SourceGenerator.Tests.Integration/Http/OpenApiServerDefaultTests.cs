using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the OpenAPI <c>servers</c> array defaults to a gateway-reachable address when no explicit
/// server list is configured, following the precedence: explicit <see cref="OpenApiInfoOptions.Servers" />,
/// then <see cref="BlueprintHttpOptions.PublicApiHost" />. <see cref="BlueprintHttpOptions.PublicHost" /> is not a
/// fallback: it is a bare host for link generation, not the complete address including mount prefix that a
/// <c>servers</c> entry has to be.
/// The document is pre-rendered once at registration (<see cref="OpenApiDocumentEndpoint" />), so there is no
/// per-request origin available here to fall back to beyond those two configured options.
/// </summary>
public class OpenApiServerDefaultTests
{
    [Fact]
    public void When_no_servers_and_no_public_host_configured_then_servers_section_omitted()
    {
        var context = CreateOutputContext(new OpenApiInfoOptions(), new BlueprintHttpOptions());
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.TryGetProperty("servers", out _).Should().BeFalse(
            "servers should be omitted when neither explicit config nor a public host is set");
    }

    [Fact]
    public void When_explicit_servers_configured_then_explicit_servers_win_over_public_api_host()
    {
        var infoOptions = new OpenApiInfoOptions();
        infoOptions.Servers.Add(new OpenApiServerEntry { Url = "https://explicit.example.com/api" });

        var httpOptions = new BlueprintHttpOptions { PublicApiHost = "gateway.example.com/import/api" };

        var context = CreateOutputContext(infoOptions, httpOptions);
        var doc = JsonDocument.Parse(context.Json);

        var servers = doc.RootElement.GetProperty("servers").EnumerateArray().ToArray();
        servers.Should().HaveCount(1);
        servers[0].GetProperty("url").GetString().Should().Be("https://explicit.example.com/api",
            "explicit Servers configuration must take precedence over PublicApiHost");
    }

    [Fact]
    public void When_public_api_host_configured_then_default_server_uses_public_api_host()
    {
        var httpOptions = new BlueprintHttpOptions { PublicApiHost = "gateway.example.com/import/api" };

        var context = CreateOutputContext(new OpenApiInfoOptions(), httpOptions);
        var doc = JsonDocument.Parse(context.Json);

        var servers = doc.RootElement.GetProperty("servers").EnumerateArray().ToArray();
        servers.Should().HaveCount(1);
        servers[0].GetProperty("url").GetString().Should().Be("https://gateway.example.com/import/api",
            "PublicApiHost is the gateway-reachable address clients should call");
    }

    [Fact]
    public void When_only_public_host_configured_then_servers_section_omitted()
    {
        var httpOptions = new BlueprintHttpOptions { PublicHost = "internal.example.com" };

        var context = CreateOutputContext(new OpenApiInfoOptions(), httpOptions);
        var doc = JsonDocument.Parse(context.Json);

        // PublicHost is a bare host that link generation composes a scheme and the request's PathBase around; a servers
        // entry is a complete address including any mount prefix. Advertising "https://internal.example.com" for an API
        // mounted under a prefix would point clients at a URL it does not answer on, so PublicHost is not a fallback.
        doc.RootElement.TryGetProperty("servers", out _).Should().BeFalse(
            "PublicHost is not a complete public address and must not stand in for PublicApiHost");
    }

    [Fact]
    public void When_public_api_host_and_public_host_both_configured_then_public_api_host_wins()
    {
        var httpOptions = new BlueprintHttpOptions
        {
            PublicApiHost = "gateway.example.com/import/api",
            PublicHost = "internal.example.com",
        };

        var context = CreateOutputContext(new OpenApiInfoOptions(), httpOptions);
        var doc = JsonDocument.Parse(context.Json);

        var servers = doc.RootElement.GetProperty("servers").EnumerateArray().ToArray();
        servers.Should().HaveCount(1);
        servers[0].GetProperty("url").GetString().Should().Be("https://gateway.example.com/import/api",
            "PublicApiHost is the more specific setting and should win over PublicHost");
    }

    [Fact]
    public void When_public_api_host_is_blank_then_servers_section_omitted()
    {
        var httpOptions = new BlueprintHttpOptions { PublicApiHost = "   " };

        var context = CreateOutputContext(new OpenApiInfoOptions(), httpOptions);
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.TryGetProperty("servers", out _).Should().BeFalse(
            "a blank PublicApiHost must not produce a bare scheme-only server entry");
    }

    [Fact]
    public void When_public_api_host_already_has_scheme_then_scheme_is_not_duplicated()
    {
        var httpOptions = new BlueprintHttpOptions { PublicApiHost = "https://gateway.example.com/import/api" };

        var context = CreateOutputContext(new OpenApiInfoOptions(), httpOptions);
        var doc = JsonDocument.Parse(context.Json);

        var servers = doc.RootElement.GetProperty("servers").EnumerateArray().ToArray();
        servers[0].GetProperty("url").GetString().Should().Be("https://gateway.example.com/import/api",
            "a PublicApiHost that already carries a scheme should not be prefixed again");
    }

    private static OpenApiOutputRenderingContext CreateOutputContext(OpenApiInfoOptions infoOptions, BlueprintHttpOptions httpOptions)
    {
        var documentBuilderContext = new OpenApiDocumentBuilderContext(
            new BlueprintApp(),
            null,
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiDocumentOperationContext>.Empty);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, ImmutableArray<OpenApiSchemaModel>.Empty, FrozenDictionary<string, string>.Empty);
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(schemaProjectionContext, []);
        var responseSchemaProjectionContext = new OpenApiResponseSchemaReferenceProjectionContext(
            schemaProjectionContext,
            ImmutableArray<OpenApiResponseSchemaReferenceOperationContext>.Empty);
        var responseObjectContext = new OpenApiResponseObjectRenderingContext(
            responseSchemaProjectionContext,
            schemaComponentContext,
            ImmutableArray<OpenApiResponseObjectOperationContext>.Empty);
        var operationContext = new OpenApiOperationRenderingContext(
            responseObjectContext,
            ImmutableArray<OpenApiOperationModel>.Empty);
        var pathItemContext = new OpenApiPathItemRenderingContext(operationContext, []);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext, infoOptions: infoOptions, httpOptions: httpOptions);
    }
}
