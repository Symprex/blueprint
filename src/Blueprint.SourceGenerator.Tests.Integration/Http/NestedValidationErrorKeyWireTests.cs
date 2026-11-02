using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a nested or indexed member's 422 key, which the generated pipeline's recursive validation builds, is named
/// with the app's naming policy from the members' own names.
/// </summary>
/// <remarks>
/// The recursive validator camelCases the paths it builds, as legacy Blueprint did, so a policy used to be applied to
/// <c>address.street</c> rather than <c>Address.Street</c>: one that reads the member's own casing named it unlike the
/// body's property (Symprex/blueprint#101). With no policy the keys stay camelCase.
/// </remarks>
public sealed class NestedValidationErrorKeyWireTests
{
    private const string Body = """{"address":{},"items":[{"name":"ok"},{}]}""";

    [Fact]
    public async Task A_nested_members_key_is_named_from_its_members_own_names()
    {
        await using var host = await NestedHost.StartAsync(static json => json.DictionaryKeyPolicy = new PrefixNamingPolicy());

        var errors = await host.PostAsync(Body);

        errors.Should().BeEquivalentTo("x_Address.x_Street", "x_Address.x_PostCode", "x_Items[1].x_Name");
    }

    [Fact]
    public async Task With_no_naming_policy_a_nested_members_key_is_camelCase_as_before()
    {
        await using var host = await NestedHost.StartAsync(static json =>
        {
            json.PropertyNamingPolicy = null;
            json.DictionaryKeyPolicy = null;
        });

        var errors = await host.PostAsync(Body);

        errors.Should().BeEquivalentTo("address.street", "address.postCode", "items[1].name");
    }

    /// <summary>A naming policy whose output shows the casing of the name it was given.</summary>
    private sealed class PrefixNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return "x_" + name;
        }
    }

    /// <summary>The integration app, served by Kestrel with the given serializer options.</summary>
    private sealed class NestedHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private NestedHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<NestedHost> StartAsync(Action<JsonSerializerOptions> configure)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint => blueprint
                .AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>()
                .ConfigureJson(json => configure(json.SerializerOptions)));

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new NestedHost(app, client);
        }

        public async Task<string[]> PostAsync(string body)
        {
            using var response = await this._client.PostAsync("nested-validation", new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json"));

            ((int)response.StatusCode).Should().Be(422);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return document.RootElement.GetProperty("errors").EnumerateObject().Select(static property => property.Name).ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A command whose body holds a child object and a list of child objects, each validated recursively.</summary>
[AllowAnonymous]
[RootLink("/nested-validation")]
[HttpPost]
public partial class PostNestedValidationCommand : ICommand<string>
{
    public NestedValidationAddress? Address { get; set; }

    public List<NestedValidationItem>? Items { get; set; }

    public static string Handle(PostNestedValidationCommand command)
    {
        return "valid";
    }
}

/// <summary>A child object with validated members.</summary>
public sealed class NestedValidationAddress
{
    [Required]
    public string? Street { get; set; }

    [Required]
    public string? PostCode { get; set; }
}

/// <summary>A list item with a validated member.</summary>
public sealed class NestedValidationItem
{
    [Required]
    public string? Name { get; set; }
}
