using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>Serialises the tests that read or clear the process-wide XML documentation cache.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OpenApiXmlDocsCacheCollection
{
    public const string Name = "OpenApiXmlDocs cache";
}

/// <summary>
/// Proves what a process keeps of the XML documentation file once it has described its API: little while it holds the
/// documentation, and nothing once the document is rendered unless the host asks to keep it.
/// </summary>
/// <remarks>
/// Signature365's idle heap dumps held 17 MB of an 50 MB Portal heap in the parsed documentation trees of every
/// assembly, which nothing read after the document was rendered.
/// </remarks>
[Collection(OpenApiXmlDocsCacheCollection.Name)]
public sealed class OpenApiXmlDocsMemoryTests
{
    [Fact]
    public void When_documentation_is_held_then_it_costs_less_than_twice_the_file_it_came_from()
    {
        OpenApiXmlDocs.Release();
        var xmlLength = new FileInfo(Path.ChangeExtension(typeof(Link).Assembly.Location, ".xml")).Length;
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var summary = OpenApiXmlDocs.GetSchemaSummary(typeof(Link));
        var held = GC.GetTotalMemory(forceFullCollection: true) - before;

        summary.Should().NotBeNullOrEmpty();
        OpenApiXmlDocs.CachedAssemblyCount.Should().BeGreaterThan(0);

        // The parsed tree of a documentation file was nearly four times its size on disk (986,224 bytes for a 251,329 byte
        // file): a node per element, text run and attribute, each with a string. Kept as the strings read, it is about
        // the file's size, as UTF-16 text makes up for the parameters, returns and methods that are not kept.
        held.Should().BeLessThan(xmlLength * 2, "only the summaries and remarks that are described are kept, as strings");
    }

    [Fact]
    public void When_a_document_is_rendered_then_the_documentation_is_released()
    {
        OpenApiXmlDocs.Release();

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        rendered.Json.Should().Contain("\"openapi\"");
        OpenApiXmlDocs.CachedAssemblyCount.Should().Be(0);
    }

    [Fact]
    public void When_the_host_retains_documentation_then_it_is_kept_after_rendering()
    {
        OpenApiXmlDocs.Release();

        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions(),
            httpOptions: new BlueprintHttpOptions { RetainXmlDocumentationAfterRender = true });

        rendered.Json.Should().Contain("\"openapi\"");
        OpenApiXmlDocs.CachedAssemblyCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void When_the_documentation_was_released_then_the_next_description_reads_it_again()
    {
        OpenApiXmlDocs.Release();
        var first = OpenApiXmlDocs.GetSchemaSummary(typeof(Link));

        OpenApiXmlDocs.Release();

        OpenApiXmlDocs.GetSchemaSummary(typeof(Link)).Should().Be(first).And.NotBeNullOrEmpty();
    }
}
