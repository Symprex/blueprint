using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using BlueprintValidationException = Blueprint.Validation.ValidationException;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a 422's <c>errors</c> keys are named with the app's JSON naming policy - its
/// <see cref="JsonSerializerOptions.DictionaryKeyPolicy" />, or else its <see cref="JsonSerializerOptions.PropertyNamingPolicy" />
/// - converting every segment of a member path, and are written as the member names when it has neither.
/// </summary>
/// <remarks>
/// The keys used to be the CLR member names whatever the app's policy, so a camelCase API answered <c>"Name"</c> for the
/// property it writes as <c>"name"</c>, and a client could not place the error on its field (Symprex/blueprint#91).
/// Legacy Blueprint wrote camelCase keys, and Timestamp's SPA matches <c>errors[key]</c> against its camelCase model.
/// </remarks>
public sealed class ValidationErrorKeyNamingTests
{
    private static readonly string[] _memberPaths = ["Name", "Address.PostCode", "Items[0].Name", "Items[12].UnitPrice"];

    public static TheoryData<string> Paths => new() { "grouped", "pre-built" };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task With_a_camelCase_property_naming_policy_every_segment_of_a_key_is_camelCase(string path)
    {
        var errors = await WriteValidationFailureAsync(path, static json => json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        errors.Keys.Should().BeEquivalentTo("name", "address.postCode", "items[0].name", "items[12].unitPrice");
        errors["items[0].name"].Should().Equal("Items[0].Name is invalid");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task The_dictionary_key_policy_is_preferred_to_the_property_naming_policy(string path)
    {
        var errors = await WriteValidationFailureAsync(path, static json =>
        {
            json.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            json.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        });

        errors.Keys.Should().BeEquivalentTo("name", "address.postCode", "items[0].name", "items[12].unitPrice");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task The_dictionary_key_policy_is_applied_to_each_segment_once(string path)
    {
        // System.Text.Json applies a dictionary key policy to a dictionary's keys itself, to the whole key: it used to
        // name "Items[0].Name" "x_Items[0].Name". Applied again on top of the segments' naming, it would be "x_x_Items...".
        var errors = await WriteValidationFailureAsync(path, static json => json.DictionaryKeyPolicy = new PrefixNamingPolicy());

        errors.Keys.Should().BeEquivalentTo("x_Name", "x_Address.x_PostCode", "x_Items[0].x_Name", "x_Items[12].x_UnitPrice");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task With_no_naming_policy_the_keys_are_the_member_paths(string path)
    {
        var errors = await WriteValidationFailureAsync(path, static json =>
        {
            json.PropertyNamingPolicy = null;
            json.DictionaryKeyPolicy = null;
        });

        errors.Keys.Should().BeEquivalentTo(_memberPaths);
    }

    [Theory]
    [InlineData("grouped", "Items[\"a]B\"].Name", "items[\"a]B\"].name")]
    [InlineData("grouped", "Items[\"a[B\"].Name", "items[\"a[B\"].name")]
    [InlineData("grouped", "Items['a]B'].Name", "items['a]B'].name")]
    [InlineData("grouped", "Items[\"a\\\"]B\"].Name", "items[\"a\\\"]B\"].name")]
    [InlineData("pre-built", "Items[\"a]B\"].Name", "items[\"a]B\"].name")]
    [InlineData("pre-built", "Items[\"a[B\"].Name", "items[\"a[B\"].name")]
    [InlineData("grouped", "Items[O'Brien].Name", "items[O'Brien].name")]
    [InlineData("pre-built", "Items[O'Brien].Name", "items[O'Brien].name")]
    public async Task An_indexer_key_is_kept_whatever_brackets_or_quotes_it_holds(string path, string memberPath, string expected)
    {
        // A dictionary key is kept as it is, so a bracket quoted inside it must neither end the indexer early (renaming
        // the rest of the key) nor open another one (leaving the member after it unnamed), and an apostrophe inside an
        // unquoted key is not a quote.
        var result = path == "grouped"
            ? new ValidationFailedResult([new ValidationResult("Invalid", [memberPath])])
            : new ValidationFailedResult(new BlueprintValidationException(new Dictionary<string, string[]> { [memberPath] = ["Invalid"] }));

        var errors = await WriteAsync(result, static json => json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        errors.Keys.Should().Equal(expected);
    }

    [Fact]
    public async Task A_validation_exception_of_nested_results_is_named_from_the_members_own_names()
    {
        // The exception's error dictionary is keyed by the recursive validator's camelCased paths, which a policy used to
        // name: x_address.x_street rather than the body's x_Address.x_Street.
        var nested = RecursiveDataAnnotationValidator.ValidateChildObject(
            "Address",
            new NestedValidationAddress { PostCode = "AB1 2CD" },
            new ValidationContext(new object()));

        var errors = await WriteAsync(
            new ValidationFailedResult(new BlueprintValidationException(nested)),
            static json => json.DictionaryKeyPolicy = new PrefixNamingPolicy());

        errors.Keys.Should().Equal("x_Address.x_Street");
    }

    [Fact]
    public async Task A_data_annotations_validation_exception_of_a_nested_result_is_named_from_the_members_own_names()
    {
        // The pipeline turns a thrown DataAnnotations ValidationException into a result of its own, which used to keep only
        // the camelCased member name: x_address.x_street rather than the body's x_Address.x_Street.
        var nested = RecursiveDataAnnotationValidator.ValidateChildObject(
            "Address",
            new NestedValidationAddress { PostCode = "AB1 2CD" },
            new ValidationContext(new object())).Single();

        var result = Blueprint.Middleware.ValidationMiddlewareBuilder.ToValidationFailedResult(
            new System.ComponentModel.DataAnnotations.ValidationException(nested, null, null));

        var errors = await WriteAsync(result, static json => json.DictionaryKeyPolicy = new PrefixNamingPolicy());

        errors.Keys.Should().Equal("x_Address.x_Street");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Nested_members_whose_names_differ_only_in_case_keep_their_own_messages(string path)
    {
        // Both members' camelCased paths are address.name, the one key of the exception's dictionary, which used to put
        // both messages under the first member's path and none under the second's.
        var nested = RecursiveDataAnnotationValidator.ValidateChildObject(
            "Address",
            new CaseCollidingAddress(),
            new ValidationContext(new object())).ToList();

        var result = path == "grouped"
            ? new ValidationFailedResult(nested)
            : new ValidationFailedResult(new BlueprintValidationException(nested));

        var errors = await WriteAsync(result, static json => json.DictionaryKeyPolicy = new PrefixNamingPolicy());

        errors.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["x_Address.x_Name"] = ["Upper"],
            ["x_Address.x_name"] = ["Lower"],
        });
    }

    [Fact]
    public async Task Keys_that_a_policy_makes_equal_are_written_as_one_with_both_messages()
    {
        var results = new[]
        {
            new ValidationResult("First", ["Name"]),
            new ValidationResult("Second", ["name"]),
        };

        var errors = await WriteAsync(new ValidationFailedResult(results), static json => json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        errors.Keys.Should().Equal("name");
        errors["name"].Should().Equal("First", "Second");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task The_form_level_key_is_not_renamed_by_a_policy(string path)
    {
        const string message = "The form as a whole is invalid";

        var result = path == "grouped"
            ? new ValidationFailedResult([new ValidationResult(message)])
            : new ValidationFailedResult(new BlueprintValidationException(message));

        var errors = await WriteAsync(result, static json => json.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseUpper);

        errors.Keys.Should().Equal("form");
    }

    private static Task<Dictionary<string, string[]>> WriteValidationFailureAsync(string path, Action<JsonSerializerOptions> configure)
    {
        var result = path == "grouped"
            ? new ValidationFailedResult(_memberPaths.Select(static member => new ValidationResult(member + " is invalid", [member])))
            : new ValidationFailedResult(new BlueprintValidationException(
                _memberPaths.ToDictionary(static member => member, static member => new[] { member + " is invalid" })));

        return WriteAsync(result, configure);
    }

    private static async Task<Dictionary<string, string[]>> WriteAsync(ValidationFailedResult result, Action<JsonSerializerOptions> configure)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                blueprint => blueprint.ConfigureJson(json => configure(json.SerializerOptions)))
            .BuildServiceProvider();

        var app = services.GetRequiredService<IntegrationTestBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(GetFormLevelValidationQuery)), CancellationToken.None);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };

        await new HttpOperationResultExecutor().TryExecuteAsync(httpContext, context, result);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(((MemoryStream)httpContext.Response.Body).ToArray()));

        return document.RootElement.GetProperty("errors").EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => property.Value.EnumerateArray().Select(static message => message.GetString()!).ToArray(),
            StringComparer.Ordinal);
    }

    /// <summary>A child object with two members whose names differ only in case, as a case-sensitive body can have.</summary>
    public sealed class CaseCollidingAddress
    {
        [Required(ErrorMessage = "Upper")]
        public string? Name { get; set; }

#pragma warning disable SA1300, IDE1006 // The member's casing is the point.
        [Required(ErrorMessage = "Lower")]
        public string? name { get; set; }
#pragma warning restore SA1300, IDE1006
    }

    /// <summary>A naming policy that renames a name it has already renamed, so applying it twice shows.</summary>
    private sealed class PrefixNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return "x_" + name;
        }
    }
}
