using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelSerializerContextTests
{
    [Fact]
    public void When_app_declares_serializer_context_then_constructed_app_exposes_declared_serializer_context_type()
    {
        var app = new IntegrationTestBlueprintApp();

        app.AppOwnedSerializerContextType.Should().Be(typeof(IntegrationTestAppJsonContext));
    }

    [Fact]
    public void When_app_declares_serializer_context_then_constructed_app_exposes_declared_serializer_context_body_type_names()
    {
        var app = new IntegrationTestBlueprintApp();

        app.AppOwnedSerializerContextBodyTypeNames.Should().Equal(
            "string",
            "System.Guid",
            "Microsoft.AspNetCore.Mvc.ProblemDetails",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.LinkTestApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MappedLinkApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.ResourceWithoutSelfLink",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.CatchAllFolderApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EWidgetResponse",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EJobAcceptedPayload",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EConflictDetail",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.ExpandableTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.AlwaysExpandTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MultiExpandableTestEmployee");
    }

    [Fact]
    public void When_app_declares_serializer_context_then_constructed_app_exposes_declared_serializer_context_instance()
    {
        var app = new IntegrationTestBlueprintApp();

        app.AppOwnedSerializerContext.Should().NotBeNull();
        app.AppOwnedSerializerContext.Should().BeOfType<IntegrationTestAppJsonContext>();
    }

    [Fact]
    public void When_app_declares_serializer_context_then_constructed_app_exposes_declared_serializer_context_body_types()
    {
        var app = new IntegrationTestBlueprintApp();

        app.AppOwnedSerializerContextBodyTypes.Should().Equal(
            typeof(string),
            typeof(Guid),
            typeof(Microsoft.AspNetCore.Mvc.ProblemDetails),
            typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails),
            typeof(LinkTestApiResource),
            typeof(MappedLinkApiResource),
            typeof(ResourceWithoutSelfLink),
            typeof(CatchAllFolderApiResource),
            typeof(E2EWidgetResponse),
            typeof(E2EJobAcceptedPayload),
            typeof(E2EConflictDetail),
            typeof(ExpandableTestEmployee),
            typeof(AlwaysExpandTestEmployee),
            typeof(MultiExpandableTestEmployee));
    }
}
