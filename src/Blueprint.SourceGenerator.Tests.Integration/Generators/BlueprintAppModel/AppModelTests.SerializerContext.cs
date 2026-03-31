using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
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
            "Microsoft.AspNetCore.Mvc.ValidationProblemDetails");
    }
}
