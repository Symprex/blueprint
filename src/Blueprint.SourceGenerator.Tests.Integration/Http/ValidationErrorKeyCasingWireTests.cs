using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlueprintValidationException = Blueprint.Validation.ValidationException;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the keys of a 422's <c>errors</c> object are camelCased, as legacy Blueprint wrote them.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>Blueprint</c>) wrote each key as <c>options.PropertyNamingPolicy?.ConvertName(kvp.Key) ?? kvp.Key</c>
/// in its <c>ValidationProblemDetailsJsonConverter</c>, so a <c>[Required] string Name</c> failure was keyed <c>name</c> and a
/// <c>[Range] int MaxResults</c> failure <c>maxResults</c>. The form-level key <c>form</c> is already lower case.
/// </remarks>
public sealed class ValidationErrorKeyCasingWireTests
{
    [Fact]
    public async Task A_data_annotation_failure_is_keyed_by_the_camel_cased_property_name()
    {
        await using var host = await CasingHost.StartAsync();

        var (status, body) = await host.GetAsync("error-casing/annotations");

        status.Should().Be(422, body);
        KeysOf(body).Should().BeEquivalentTo("name", "maxResults");
    }

    [Fact]
    public async Task A_thrown_ValidationException_is_keyed_by_the_camel_cased_member_name()
    {
        await using var host = await CasingHost.StartAsync();

        var (status, body) = await host.GetAsync("error-casing/thrown");

        status.Should().Be(422, body);
        KeysOf(body).Should().BeEquivalentTo("name", "maxResults");
    }

    [Fact]
    public async Task The_form_level_key_stays_form()
    {
        await using var host = await CasingHost.StartAsync();

        var (status, body) = await host.GetAsync("error-casing/form");

        status.Should().Be(422, body);
        KeysOf(body).Should().Equal("form");
    }

    private static string[] KeysOf(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("errors").EnumerateObject().Select(static p => p.Name).ToArray();
    }

    /// <summary>The integration app, served by Kestrel.</summary>
    private sealed class CasingHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private CasingHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<CasingHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new CasingHost(app, client);
        }

        public async Task<(int Status, string Body)> GetAsync(string path)
        {
            using var response = await this._client.GetAsync(path);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A query whose PascalCase properties fail data-annotation validation.</summary>
[AllowAnonymous]
[RootLink("/error-casing/annotations")]
public partial class GetErrorCasingAnnotationsQuery : IQuery<string>
{
    [Required]
    public string Name { get; set; } = null!;

    [Range(1, 100)]
    public int MaxResults { get; set; }

    public static string Handle(GetErrorCasingAnnotationsQuery query)
    {
        return "valid";
    }
}

/// <summary>A query whose handler throws a <see cref="BlueprintValidationException"/> keyed by PascalCase member names.</summary>
[AllowAnonymous]
[RootLink("/error-casing/thrown")]
public partial class GetErrorCasingThrownQuery : IQuery<string>
{
    public static string Handle(GetErrorCasingThrownQuery query)
    {
        throw new BlueprintValidationException(
            [
                new ValidationResult("Name is required", ["Name"]),
                new ValidationResult("MaxResults is out of range", ["MaxResults"]),
            ]);
    }
}

/// <summary>A query whose handler throws a form-level <see cref="BlueprintValidationException"/>.</summary>
[AllowAnonymous]
[RootLink("/error-casing/form")]
public partial class GetErrorCasingFormQuery : IQuery<string>
{
    public static string Handle(GetErrorCasingFormQuery query)
    {
        throw new BlueprintValidationException("The form as a whole is invalid");
    }
}
