using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTestsMissingSerializerContextDeclarationValidation
{
    [Fact]
    public async Task When_http_visible_app_does_not_declare_serializer_context_then_generator_reports_diagnostic()
    {
        var source = """
                     #nullable enable
                     using System;
                     using Blueprint.Model;
                     using Blueprint.Model.Attributes;
                     using Blueprint.Runtime.Exec;

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

                         [RootLink("/serializer-context/missing-declaration")]
                         public partial class IncludedOperation : IQuery<OkResult<MissingPayload>>
                         {
                             public static ApiOperationDescriptor Configure() => throw new NotImplementedException();
                         }

                         [BlueprintAppScan]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                     }
                     """;

        var result = TestHelpers.Compile<AppDataModelSourceGenerator>(source);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .Contain("BP007");

        await result.VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public async Task When_app_has_no_http_serializer_coverage_requirements_then_missing_serializer_context_is_not_reported()
    {
        var source = """
                     #nullable enable
                     using System;
                     using Blueprint.Model;

                     namespace TestNamespace
                     {
                         public partial class IncludedOperation : IOperation
                         {
                             public static ApiOperationDescriptor Configure() => throw new NotImplementedException();
                         }

                         [BlueprintAppScan]
                         public partial class TestBlueprintApp : BlueprintApp
                         {
                         }
                     }
                     """;

        var result = TestHelpers.Compile<AppDataModelSourceGenerator>(source);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id)
            .Should()
            .NotContain("BP007");

        await result.VerifySuccessAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }
}
