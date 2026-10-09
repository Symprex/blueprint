using System.Net;
using System.Net.Http.Headers;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves that <see cref="BlueprintHttpClient{TApp}"/> reads back every resource event type the host writes, the
/// <see cref="ResourceCreated{TResource}"/>, <see cref="ResourceUpdated{TResource}"/> and
/// <see cref="ResourceDeleted{TResource}"/> subclasses included, as legacy Blueprint's client read them.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's client read a response with the app's <c>BlueprintJsonOptions</c> (Signature365 <c>8b40ecc961</c>,
/// <c>Common.Core/Api/BlueprintHttpClient.cs:40-44</c>, <c>:201-210</c>), whose
/// <c>JsonConverterFactoryForResourceEventOfT</c> (registered by <c>Blueprint.Http/BlueprintJsonOptions.cs</c>) read any
/// generic type assignable to <c>ResourceEvent</c> (<c>JsonConverterFactoryForResourceEventOfT.cs:16-20</c>): an uninitialised
/// instance of the requested type (<c>:61</c>), its resource type the generic argument (<c>:64</c>), and each wire property
/// read by name (<c>:127-169</c>), a dictionary's numbers as <c>int</c>, strings as <c>string</c> and booleans as
/// <c>bool</c> (<c>:238-250</c>).
/// </para>
/// <para>
/// Each body here is written by the host itself, <c>HttpOperationResultExecutor</c>, from the event a handler returns.
/// </para>
/// </remarks>
public sealed class BlueprintHttpClientResourceEventTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientResourceEventTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task When_the_host_wrote_a_resource_created_event_then_it_is_read_back_as_one()
    {
        var written = Stamp(new ResourceCreated<TemplateApiResource>(Template()));

        var read = await this.RoundTripAsync<ResourceCreated<TemplateApiResource>>(written);

        AssertReadAsWritten(read, written, "template.created", ResourceEventChangeType.Created);
    }

    [Fact]
    public async Task When_the_host_wrote_a_resource_updated_event_then_it_is_read_back_as_one()
    {
        var written = Stamp(new ResourceUpdated<TemplateApiResource>("renamed", Template()));

        var read = await this.RoundTripAsync<ResourceUpdated<TemplateApiResource>>(written);

        AssertReadAsWritten(read, written, "template.renamed", ResourceEventChangeType.Updated);
    }

    [Fact]
    public async Task When_the_host_wrote_a_resource_deleted_event_then_it_is_read_back_as_one()
    {
        var written = Stamp(new ResourceDeleted<TemplateApiResource>(Template()));

        var read = await this.RoundTripAsync<ResourceDeleted<TemplateApiResource>>(written);

        AssertReadAsWritten(read, written, "template.deleted", ResourceEventChangeType.Deleted);
    }

    [Fact]
    public async Task When_the_host_wrote_a_resource_event_then_its_metadata_values_are_read_as_legacy_read_them()
    {
        // JsonConverterFactoryForResourceEventOfT.cs:238-250: a number as an int, a string as a string, a boolean as a bool.
        var written = Stamp(new ResourceEvent<TemplateApiResource>(ResourceEventChangeType.Updated, "template.updated", Template()));

        var read = await this.RoundTripAsync<ResourceEvent<TemplateApiResource>>(written);

        read!.Metadata["UserId"].Should().BeOfType<string>().Which.Should().Be("user-1");
        read.Metadata["Attempts"].Should().BeOfType<int>().Which.Should().Be(3);
        read.Metadata["Elevated"].Should().BeOfType<bool>().Which.Should().BeTrue();
        read.Created.Should().Be(written.Created, "legacy read the event's own created date (:137-140), not the time it was read");
    }

    [Fact]
    public async Task When_a_results_member_is_a_resource_created_event_then_it_is_read_back_as_one()
    {
        var written = Stamp(new ResourceCreated<TemplateApiResource>(Template()));

        var read = await this.RoundTripAsync<Results<StatusCodeResult.Accepted, ResourceCreated<TemplateApiResource>>>(written);

        read.IsT2.Should().BeTrue();
        AssertReadAsWritten(read.AsT2, written, "template.created", ResourceEventChangeType.Created);
    }

    [Fact]
    public async Task When_the_envelope_reader_reads_a_resource_event_then_its_dictionaries_are_read_as_SendAsync_reads_them()
    {
        // SendAsync reads a resource event's dictionaries as legacy's converter did (JsonConverterFactoryForResourceEventOfT.cs:
        // 231-263): a number as an int, a string as a string, a boolean as a bool, and, where legacy threw, a number that is
        // no int as a double (ResourceEventReader). The envelope reader, new-only, reads the same body the same way.
        var written = Stamp(new ResourceUpdated<TemplateApiResource>("renamed", Template()));
        written.WithMetadata("Ratio", 2.5).WithSecureData("Pin", 1234);
        written.ChangedValues = new Dictionary<string, object> { ["Revision"] = 7, ["Name"] = "Old" };
        var (body, mediaType) = await this.WriteAsTheHostWritesAsync(written);
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse(mediaType) } },
        }));

        var envelope = await client.SendResourceEventEnvelopeAsync<TemplateApiResource>(new ArchiveTemplateCommand { Id = "t-1" });
        var read = await client.SendAsync<ResourceEvent<TemplateApiResource>>(new ArchiveTemplateCommand { Id = "t-1" });

        foreach (var readEvent in new[] { envelope!, read! })
        {
            readEvent.Metadata["UserId"].Should().BeOfType<string>().Which.Should().Be("user-1");
            readEvent.Metadata["Attempts"].Should().BeOfType<int>().Which.Should().Be(3);
            readEvent.Metadata["Elevated"].Should().BeOfType<bool>().Which.Should().BeTrue();
            readEvent.Metadata["Ratio"].Should().BeOfType<double>().Which.Should().Be(2.5);
            readEvent.SecureData["Pin"].Should().BeOfType<int>().Which.Should().Be(1234);
            readEvent.ChangedValues!["Revision"].Should().BeOfType<int>().Which.Should().Be(7);
            readEvent.ChangedValues["Name"].Should().BeOfType<string>().Which.Should().Be("Old");
        }
    }

    [Fact]
    public async Task When_the_host_wrote_a_deleted_event_with_no_data_then_every_reader_reads_no_resource()
    {
        // A deleted event's self query is never run, so it has no data, and the host writes no data key for it, as legacy
        // did (JsonConverterFactoryForResourceEventOfT.cs, the Data != null check). Each reader reads that as it read the
        // "data": null the host used to write: no resource, not the envelope itself read as one.
        var written = Stamp(new ResourceDeleted<TemplateApiResource>(new GetTemplateQuery { Id = "t-1" }));
        var (body, mediaType) = await this.WriteAsTheHostWritesAsync(written);
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse(mediaType) } },
        }));

        using (var document = System.Text.Json.JsonDocument.Parse(body))
        {
            document.RootElement.TryGetProperty("data", out _).Should().BeFalse("the host writes no data key for an event with none");
        }

        var resource = await client.SendResourceEventAsync<TemplateApiResource>(new ArchiveTemplateCommand { Id = "t-1" });
        var envelope = await client.SendResourceEventEnvelopeAsync<TemplateApiResource>(new ArchiveTemplateCommand { Id = "t-1" });
        var read = await client.SendAsync<ResourceDeleted<TemplateApiResource>>(new ArchiveTemplateCommand { Id = "t-1" });

        resource.Should().BeNull();
        envelope.Should().BeNull("an envelope with no data is read as no envelope, as one with null data was");
        read.Should().NotBeNull();
        read!.EventId.Should().Be("template.deleted");
        read.Data.Should().BeNull();
    }

    private static TemplateApiResource Template()
    {
        return new TemplateApiResource("t-1", "Welcome", "<p>Hi</p>", "Hi");
    }

    private static T Stamp<T>(T resourceEvent)
        where T : ResourceEvent
    {
        // As the host's pipeline stamps a command's event before it is written.
        resourceEvent.Href = "https://api.example.com/templates/t-1";
        resourceEvent.CorrelationId = "00-trace-span-01";
        resourceEvent.WithMetadata("UserId", "user-1").WithMetadata("Attempts", 3).WithMetadata("Elevated", true);

        return resourceEvent;
    }

    private static void AssertReadAsWritten<T>(T? read, T written, string eventId, ResourceEventChangeType changeType)
        where T : ResourceEvent<TemplateApiResource>
    {
        read.Should().NotBeNull().And.BeOfType<T>("legacy made an instance of the requested type (:61)");
        read!.EventId.Should().Be(eventId);
        read.ChangeType.Should().Be(changeType);
        read.Created.Should().Be(written.Created);
        read.ResourceObject.Should().Be("template");
        read.Href.Should().Be("https://api.example.com/templates/t-1");
        read.CorrelationId.Should().Be("00-trace-span-01");
        read.Data!.Id.Should().Be("t-1");
        read.Data.Name.Should().Be("Welcome");
        read.Metadata.Should().ContainKey("UserId").WhoseValue.Should().Be("user-1");
    }

    private async Task<T?> RoundTripAsync<T>(ResourceEvent written)
    {
        var (body, mediaType) = await this.WriteAsTheHostWritesAsync(written);
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = MediaTypeHeaderValue.Parse(mediaType) } },
        }));

        return await client.SendAsync<T>(new ArchiveTemplateCommand { Id = "t-1" });
    }

    private async Task<(byte[] Body, string MediaType)> WriteAsTheHostWritesAsync(ResourceEvent written)
    {
        // The app as AddBlueprintApi registers it, its serializer options Blueprint's defaults.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>()
            .BuildServiceProvider();
        var app = services.GetRequiredService<SampleBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(ArchiveTemplateCommand)), CancellationToken.None);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        (await new HttpOperationResultExecutor().TryExecuteAsync(httpContext, context, written)).Should().BeTrue();
        httpContext.Response.StatusCode.Should().Be(200, "legacy sent every resource event as a 200");

        return (body.ToArray(), httpContext.Response.ContentType!);
    }
}
