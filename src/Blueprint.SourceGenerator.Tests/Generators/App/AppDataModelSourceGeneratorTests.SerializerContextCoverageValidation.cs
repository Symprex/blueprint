using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTestsSerializerContextCoverageValidation
{
    [Fact]
    public async Task When_app_serializer_context_omits_required_http_response_body_type_then_generator_reports_diagnostic()
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
                         public sealed class MissingPayload
                         {
                         }

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

                         [RootLink("/serializer-context-coverage/missing")]
                         public partial class IncludedOperation : IQuery<OkResult<MissingPayload>>
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
            .Contain("BP006");

        await result.VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_app_serializer_context_covers_required_http_response_body_type_then_generator_does_not_report_diagnostic()
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
                         public sealed class MissingPayload
                         {
                         }

                         [JsonSerializable(typeof(MissingPayload))]
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

                         [RootLink("/serializer-context-coverage/included")]
                         public partial class IncludedOperation : IQuery<OkResult<MissingPayload>>
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
            .NotContain("BP006");

        await result.VerifySuccessAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }
}
