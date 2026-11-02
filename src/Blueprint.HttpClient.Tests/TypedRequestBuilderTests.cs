using Blueprint.Model;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The typed request builders each operation's generated partial carries (<c>Op.Http.BuildUrl</c> and, for a GET,
/// <c>Op.Http.AppendQueryString</c>) build exactly what the descriptor's <see cref="CompiledHttpRequestFeatureData"/>
/// delegates build, so a typed client that calls them directly puts the same URL on the wire as
/// <see cref="BlueprintHttpClient{TApp}"/>.
/// </summary>
public sealed class TypedRequestBuilderTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public TypedRequestBuilderTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public void BuildUrl_escapes_a_route_value_as_the_descriptor_delegate_does()
    {
        var operation = new GetTemplateQuery { Id = "t 1/x" };

        var url = GetTemplateQuery.Http.BuildUrl(operation);

        url.Should().Be("/templates/t%201%2Fx");
        url.Should().Be(this.Compiled<GetTemplateQuery>().BuildUrl(operation));
    }

    [Fact]
    public void BuildUrl_substitutes_every_placeholder_of_a_multi_segment_route()
    {
        var operation = new UpdateTemplateSettingsCommand { TenantId = "acme", Id = "t-7" };

        var url = UpdateTemplateSettingsCommand.Http.BuildUrl(operation);

        url.Should().Be("/tenants/acme/templates/t-7/settings");
        url.Should().Be(this.Compiled<UpdateTemplateSettingsCommand>().BuildUrl(operation));
    }

    [Fact]
    public void BuildUrl_leaves_a_catch_all_value_unescaped()
    {
        var operation = new GetFolderContentsQuery { FolderPath = "a/b c" };

        var url = GetFolderContentsQuery.Http.BuildUrl(operation);

        url.Should().Be("/folders/a/b c");
        url.Should().Be(this.Compiled<GetFolderContentsQuery>().BuildUrl(operation));
    }

    [Fact]
    public void BuildUrl_is_null_when_a_route_value_is_null()
    {
        var operation = new GetTemplateQuery { Id = null! };

        GetTemplateQuery.Http.BuildUrl(operation).Should().BeNull();
        this.Compiled<GetTemplateQuery>().BuildUrl(operation).Should().BeNull();
    }

    [Fact]
    public void AppendQueryString_builds_the_descriptor_delegates_query_string()
    {
        var operation = new SearchTemplatesQuery
        {
            Q = "a&b",
            Page = 2,
            PageSize = 10,
            Tags = ["x", "y z"],
        };

        var url = SearchTemplatesQuery.Http.AppendQueryString(operation, SearchTemplatesQuery.Http.BuildUrl(operation)!);

        url.Should().Be(this.Compiled<SearchTemplatesQuery>().AppendQueryString!(operation, "/templates/search"));
        url.Should().StartWith("/templates/search?");
        url.Should().Contain("q=a%26b");
        url.Should().Contain("tags=x&tags=y%20z");
    }

    [Fact]
    public void AppendQueryString_appends_to_a_url_that_already_has_a_query()
    {
        var operation = new GetTemplateHistoryQuery { Id = "t-1", AuthorFilter = "ann" };

        var url = GetTemplateHistoryQuery.Http.AppendQueryString(operation, "/templates/t-1/history?x=1");

        url.Should().Be("/templates/t-1/history?x=1&authorFilter=ann");
        url.Should().Be(this.Compiled<GetTemplateHistoryQuery>().AppendQueryString!(operation, "/templates/t-1/history?x=1"));
    }

    private CompiledHttpRequestFeatureData Compiled<TOperation>()
    {
        var descriptor = this._fixture.App.FindOperation(typeof(TOperation))!;

        descriptor.TryGetFeatureData<CompiledHttpRequestFeatureData>(out var compiled).Should().BeTrue();

        return compiled!;
    }
}
