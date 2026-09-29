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
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.MultiExpandableTestEmployee",
            "Blueprint.SourceGenerator.Tests.Integration.Http.NodaTimeEchoResponse",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines.SelfQueryTestApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel.SampleWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel.UnexposedFixtureApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeTenantApiResource",
            "Blueprint.Model.PagedApiResource<Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeTenantApiResource>",
            "Blueprint.Http.ResourceEvent<Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeTenantApiResource>",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeListApiResource<Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeCountryApiResource>",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.LegacyShapeGatewayRunApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionHolderApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionTrailApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionPreviewApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionOne.CollisionItem",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionTwo.CollisionItem",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionStatusOne.CollisionStatus",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.CollisionStatusTwo.CollisionStatus",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape.ArrayEnumParameterInvoiceApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.EnumItemsResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.EnvelopeOne.EnvelopeMatch",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.EnvelopeTwo.EnvelopeMatch",
            "Blueprint.Http.ResourceEvent<Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.EnvelopeOne.EnvelopeMatch>",
            "Blueprint.Http.ResourceEvent<Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.EnvelopeTwo.EnvelopeMatch>",
            "Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions.EnvelopeThree.EnvelopeMatch",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LegacyEventWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.TypedIdWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.RequiredShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.ConverterWrittenBodyId",
            "Blueprint.SourceGenerator.Tests.Integration.Http.HrefWidgetApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.HrefWidgetSecretPartialApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.HrefUnlinkedApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.DictionaryShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.ExtensionShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.ParameterTitlesApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.XmlDocShape",
            "Blueprint.SourceGenerator.Tests.Integration.Http.NativeEventGadget",
            "Blueprint.SourceGenerator.Tests.Integration.Http.MinLengthShapeApiResource",
            "Blueprint.SourceGenerator.Tests.Integration.Http.LinkWireWidgetApiResource",
            "Blueprint.Model.ListApiResource<Blueprint.SourceGenerator.Tests.Integration.Http.LinkWireWidgetApiResource>",
            "Blueprint.SourceGenerator.Tests.Integration.Http.RelLessLinkWireWidgetApiResource");
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
            typeof(MultiExpandableTestEmployee),
            typeof(NodaTimeEchoResponse),
            typeof(SelfQueryTestApiResource),
            typeof(SampleWidgetApiResource),
            typeof(UnexposedFixtureApiResource),
            typeof(Http.LegacyShape.LegacyShapeTenantApiResource),
            typeof(Blueprint.Model.PagedApiResource<Http.LegacyShape.LegacyShapeTenantApiResource>),
            typeof(Blueprint.Http.ResourceEvent<Http.LegacyShape.LegacyShapeTenantApiResource>),
            typeof(Http.LegacyShape.LegacyShapeListApiResource<Http.LegacyShape.LegacyShapeCountryApiResource>),
            typeof(Http.LegacyShape.LegacyShapeGatewayRunApiResource),
            typeof(Http.SchemaNameCollisions.CollisionHolderApiResource),
            typeof(Http.SchemaNameCollisions.CollisionTrailApiResource),
            typeof(Http.SchemaNameCollisions.CollisionPreviewApiResource),
            typeof(Http.SchemaNameCollisions.CollisionOne.CollisionItem),
            typeof(Http.SchemaNameCollisions.CollisionTwo.CollisionItem),
            typeof(Http.SchemaNameCollisions.CollisionStatusOne.CollisionStatus),
            typeof(Http.SchemaNameCollisions.CollisionStatusTwo.CollisionStatus),
            typeof(Http.LegacyShape.ArrayEnumParameterInvoiceApiResource),
            typeof(Http.EnumItemsResource),
            typeof(Http.SchemaNameCollisions.EnvelopeOne.EnvelopeMatch),
            typeof(Http.SchemaNameCollisions.EnvelopeTwo.EnvelopeMatch),
            typeof(Blueprint.Http.ResourceEvent<Http.SchemaNameCollisions.EnvelopeOne.EnvelopeMatch>),
            typeof(Blueprint.Http.ResourceEvent<Http.SchemaNameCollisions.EnvelopeTwo.EnvelopeMatch>),
            typeof(Http.SchemaNameCollisions.EnvelopeThree.EnvelopeMatch),
            typeof(Http.LegacyEventWidgetApiResource),
            typeof(Http.TypedIdWidgetApiResource),
            typeof(Http.RequiredShapeApiResource),
            typeof(Http.ConverterWrittenBodyId),
            typeof(Http.HrefWidgetApiResource),
            typeof(Http.HrefWidgetSecretPartialApiResource),
            typeof(Http.HrefUnlinkedApiResource),
            typeof(Http.DictionaryShapeApiResource),
            typeof(Http.ExtensionShapeApiResource),
            typeof(Http.ParameterTitlesApiResource),
            typeof(Http.XmlDocShape),
            typeof(Http.NativeEventGadget),
            typeof(Http.MinLengthShapeApiResource),
            typeof(Http.LinkWireWidgetApiResource),
            typeof(Blueprint.Model.ListApiResource<Http.LinkWireWidgetApiResource>),
            typeof(Http.RelLessLinkWireWidgetApiResource));
    }
}
