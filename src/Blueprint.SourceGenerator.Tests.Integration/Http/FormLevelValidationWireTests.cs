using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlueprintValidationException = Blueprint.Validation.ValidationException;
using DataAnnotationsValidationException = System.ComponentModel.DataAnnotations.ValidationException;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a validation failure that names no member is keyed <c>form</c> in a 422's <c>errors</c>, as legacy
/// Blueprint keyed it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint</c>) named the key <c>ValidationFailures.FormLevelPropertyName =
/// "form"</c> (<c>Validation/ValidationFailures.cs:14-18</c>), and used it for:
/// </para>
/// <list type="bullet">
/// <item><description>a <c>ValidationResult</c> with no member names (:44-47);</description></item>
/// <item><description>a <c>ValidationException</c> built from a form-level message (<c>Validation/ValidationException.cs:22-31</c>)
/// or from a <c>ValidationFailedOperationResult</c>'s (<c>Middleware/ValidationFailedOperationResult.cs:21-27</c>);</description></item>
/// <item><description>a DataAnnotations <c>ValidationException</c> whose result names no member
/// (<c>Middleware/ValidationMiddlewareBuilder.cs:30-37</c>).</description></item>
/// </list>
/// <para>
/// A thrown <c>ValidationException</c> became a <c>ValidationFailedOperationResult</c> of its <c>ValidationResults</c>
/// (the generated pipeline's catch, e.g. <c>Portal/Internal/Generated/Blueprint/GetMicrosoft365AddOnQueryExecutorPipeline.cs:154-165</c>),
/// written as a <c>ValidationProblemDetails</c> of those errors (<c>Blueprint.Http/ValidationFailedOperationResultExecutor.cs:27</c>).
/// Signature365's frontend shows the <c>form</c> errors of a form it cannot place on a field
/// (<c>frontend/packages/common/src/api/undisplayedErrors.ts</c>).
/// </para>
/// </remarks>
public sealed class FormLevelValidationWireTests
{
    private const string Message = "The form as a whole is invalid";

    [Theory]
    [InlineData("validatable-object")]
    [InlineData("blueprint-validation-exception")]
    [InlineData("validation-results-exception")]
    [InlineData("validation-failures")]
    [InlineData("data-annotations-validation-exception")]
    public async Task A_failure_naming_no_member_is_keyed_form(string source)
    {
        await using var host = await FormHost.StartAsync();

        var (status, body) = await host.GetAsync(source);

        status.Should().Be(422);
        using var document = JsonDocument.Parse(body);
        var errors = document.RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(static p => p.Name).Should().Equal("form");
        errors.GetProperty("form").EnumerateArray().Select(static e => e.GetString()).Should().Equal(Message);
    }

    /// <summary>The integration app, served by Kestrel.</summary>
    private sealed class FormHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private FormHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<FormHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new FormHost(app, client);
        }

        public async Task<(int Status, string Body)> GetAsync(string source)
        {
            using var response = await this._client.GetAsync("form-validation/" + source);

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

/// <summary>A query that fails validation at the form level, in the way its <see cref="Source"/> names.</summary>
[AllowAnonymous]
[RootLink("/form-validation/{Source}")]
public partial class GetFormLevelValidationQuery : IQuery<string>, IValidatableObject
{
    private const string Message = "The form as a whole is invalid";

    public string Source { get; set; } = null!;

    public static string Handle(GetFormLevelValidationQuery query)
    {
        switch (query.Source)
        {
            case "blueprint-validation-exception":
                throw new BlueprintValidationException(Message);
            case "validation-results-exception":
                throw new BlueprintValidationException([new ValidationResult(Message)]);
            case "data-annotations-validation-exception":
                throw new DataAnnotationsValidationException(Message);
            case "validation-failures":
                var failures = new ValidationFailures();
                failures.AddFailure(new ValidationResult(Message));
                failures.ThrowIfFailed();
                break;
        }

        return "valid";
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.Source == "validatable-object")
        {
            yield return new ValidationResult(Message);
        }
    }
}
