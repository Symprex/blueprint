using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTestsUnsupportedOpenApiValidationShapes
{
    [Fact]
    public async Task When_serializer_covered_http_response_body_uses_compare_attribute_then_generator_reports_openapi_validation_diagnostic()
    {
        var source = """
                     #nullable enable

                     using System;
                     using System.ComponentModel.DataAnnotations;
                     using System.Text.Json;
                     using System.Text.Json.Serialization;
                     using System.Text.Json.Serialization.Metadata;
                     using Blueprint.Model;
                     using Blueprint.Model.Attributes;
                     using Blueprint.Runtime.Exec;
                     using Microsoft.AspNetCore.Mvc;

                     namespace Microsoft.AspNetCore.Mvc
                     {
                         public class ProblemDetails
                         {
                         }

                         public sealed class ValidationProblemDetails : ProblemDetails
                         {
                         }
                     }

                     namespace Blueprint.Model.Attributes
                     {
                         [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
                         internal sealed class RootLinkAttribute : Attribute
                         {
                             public RootLinkAttribute(string url)
                             {
                             }
                         }
                     }

                     namespace TestNamespace
                     {
                         public sealed class UnsupportedPayload
                         {
                             public string? Confirmation { get; init; }

                             [Compare(nameof(Confirmation))]
                             public string? Password { get; init; }
                         }

                         [JsonSerializable(typeof(UnsupportedPayload))]
                         [JsonSerializable(typeof(ProblemDetails))]
                         [JsonSerializable(typeof(ValidationProblemDetails))]
                         public partial class TestBlueprintJsonContext : JsonSerializerContext
                         {
                             public TestBlueprintJsonContext()
                                 : base(new JsonSerializerOptions())
                             {
                             }

                             protected override JsonSerializerOptions? GeneratedSerializerOptions => new JsonSerializerOptions();

                             public override JsonTypeInfo? GetTypeInfo(Type type)
                             {
                                 return null;
                             }
                         }

                         [RootLink("/serializer-context-coverage/unsupported-openapi-validation")]
                         public partial class IncludedOperation : IQuery<OkResult<UnsupportedPayload>>
                         {
                             public static ApiOperationDescriptor Configure() => throw new NotImplementedException();
                         }

                         [BlueprintAppScan]
                         [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext))]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                     }
                     """;

        var result = TestHelpers.Compile<AppDataModelSourceGenerator>(source);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP008");

        await result.VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_serializer_covered_http_response_body_uses_greater_than_property_attribute_then_generator_reports_openapi_validation_diagnostic()
    {
        var source = """
                     #nullable enable

                     using System;
                     using System.Text.Json;
                     using System.Text.Json.Serialization;
                     using System.Text.Json.Serialization.Metadata;
                     using Blueprint.Model;
                     using Blueprint.Model.Attributes;
                     using Blueprint.Runtime.Exec;
                     using Blueprint.Validation;
                     using Microsoft.AspNetCore.Mvc;

                     namespace Microsoft.AspNetCore.Mvc
                     {
                         public class ProblemDetails
                         {
                         }

                         public sealed class ValidationProblemDetails : ProblemDetails
                         {
                         }
                     }

                     namespace Blueprint.Model.Attributes
                     {
                         [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
                         internal sealed class RootLinkAttribute : Attribute
                         {
                             public RootLinkAttribute(string url)
                             {
                             }
                         }
                     }

                     namespace TestNamespace
                     {
                         public sealed class UnsupportedPayload
                         {
                             public int Minimum { get; init; }

                             [GreaterThanProperty(nameof(Minimum))]
                             public int Maximum { get; init; }
                         }

                         [JsonSerializable(typeof(UnsupportedPayload))]
                         [JsonSerializable(typeof(ProblemDetails))]
                         [JsonSerializable(typeof(ValidationProblemDetails))]
                         public partial class TestBlueprintJsonContext : JsonSerializerContext
                         {
                             public TestBlueprintJsonContext()
                                 : base(new JsonSerializerOptions())
                             {
                             }

                             protected override JsonSerializerOptions? GeneratedSerializerOptions => new JsonSerializerOptions();

                             public override JsonTypeInfo? GetTypeInfo(Type type)
                             {
                                 return null;
                             }
                         }

                         [RootLink("/serializer-context-coverage/unsupported-openapi-validation")]
                         public partial class IncludedOperation : IQuery<OkResult<UnsupportedPayload>>
                         {
                             public static ApiOperationDescriptor Configure() => throw new NotImplementedException();
                         }

                         [BlueprintAppScan]
                         [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext))]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                     }
                     """;

        var result = TestHelpers.Compile<AppDataModelSourceGenerator>(source);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP008");

        await result.VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }
}
