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
            "Microsoft.AspNetCore.Mvc.ProblemDetails",
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.LinkTestApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MappedLinkApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.ResourceWithoutSelfLink",
            "Blueprint.SourceGenerator.Tests.Integration.Http.E2EWidgetResponse");
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
            typeof(Microsoft.AspNetCore.Mvc.ProblemDetails),
            typeof(Microsoft.AspNetCore.Mvc.ValidationProblemDetails),
            typeof(LinkTestApiResource),
            typeof(MappedLinkApiResource),
            typeof(ResourceWithoutSelfLink),
            typeof(E2EWidgetResponse));
    }
}
