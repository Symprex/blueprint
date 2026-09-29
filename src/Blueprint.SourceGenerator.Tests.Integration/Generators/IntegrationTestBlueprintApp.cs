using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

/// <summary>
/// Provides a minimal app shell for integration fixtures that need the generator to discover operations through the same
/// app-scan entry point used by real Blueprint consumers.
/// </summary>
/// <remarks>
/// The app removes "Sample" from the name of every operation it hosts, which <c>AppModelOperationNameTransformTests</c>
/// proves through. Any fixture operation whose name contains "Sample" is renamed by this app; name one otherwise
/// unless the rename is the point.
/// <para>
/// It has only a <see cref="BoundTenantId"/> or <c>BoundTenantId?</c> <c>TenantId</c> filled from the caller's tenant, as
/// Signature365's apps have only an <c>ApiId</c> one, which <c>TenantIdBindingWireTests</c> proves through. A
/// <c>TenantId</c> of any other type is bound from the request but never filled.
/// </para>
/// </remarks>
[BlueprintAppScan]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(IntegrationTestAppJsonContext))]
[BlueprintAppOperationNames(Remove = "Sample")]
[BlueprintAppTenancy(TenantIdType = typeof(BoundTenantId))]
public partial class IntegrationTestBlueprintApp : BlueprintApp;

/// <summary>
/// Provides a minimal serializer-context declaration for integration tests that need the generated app shell to expose
/// an app-owned JSON metadata type without changing runtime serializer behavior.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
[JsonSerializable(typeof(LinkTestApiResource))]
[JsonSerializable(typeof(MappedLinkApiResource))]
[JsonSerializable(typeof(ResourceWithoutSelfLink))]
[JsonSerializable(typeof(CatchAllFolderApiResource))]
[JsonSerializable(typeof(E2EWidgetResponse))]
[JsonSerializable(typeof(E2EJobAcceptedPayload))]
[JsonSerializable(typeof(E2EConflictDetail))]
[JsonSerializable(typeof(ExpandableTestEmployee))]
[JsonSerializable(typeof(AlwaysExpandTestEmployee))]
[JsonSerializable(typeof(MultiExpandableTestEmployee))]
[JsonSerializable(typeof(Blueprint.SourceGenerator.Tests.Integration.Http.NodaTimeEchoResponse))]
[JsonSerializable(typeof(SelfQueryTestApiResource))]
[JsonSerializable(typeof(SampleWidgetApiResource))]
[JsonSerializable(typeof(UnexposedFixtureApiResource))]
[JsonSerializable(typeof(Http.LegacyShape.LegacyShapeTenantApiResource))]
[JsonSerializable(typeof(PagedApiResource<Http.LegacyShape.LegacyShapeTenantApiResource>))]
[JsonSerializable(typeof(Blueprint.Http.ResourceEvent<Http.LegacyShape.LegacyShapeTenantApiResource>))]
[JsonSerializable(typeof(Http.LegacyShape.LegacyShapeListApiResource<Http.LegacyShape.LegacyShapeCountryApiResource>))]
[JsonSerializable(typeof(Http.LegacyShape.LegacyShapeGatewayRunApiResource))]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionHolderApiResource))]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionTrailApiResource))]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionPreviewApiResource))]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionOne.CollisionItem), TypeInfoPropertyName = "CollisionOneCollisionItem")]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionTwo.CollisionItem), TypeInfoPropertyName = "CollisionTwoCollisionItem")]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionStatusOne.CollisionStatus), TypeInfoPropertyName = "CollisionStatusOneCollisionStatus")]
[JsonSerializable(typeof(Http.SchemaNameCollisions.CollisionStatusTwo.CollisionStatus), TypeInfoPropertyName = "CollisionStatusTwoCollisionStatus")]
[JsonSerializable(typeof(Http.LegacyShape.ArrayEnumParameterInvoiceApiResource))]
[JsonSerializable(typeof(Http.EnumItemsResource))]
[JsonSerializable(typeof(Http.SchemaNameCollisions.EnvelopeOne.EnvelopeMatch), TypeInfoPropertyName = "EnvelopeOneEnvelopeMatch")]
[JsonSerializable(typeof(Http.SchemaNameCollisions.EnvelopeTwo.EnvelopeMatch), TypeInfoPropertyName = "EnvelopeTwoEnvelopeMatch")]
[JsonSerializable(typeof(Blueprint.Http.ResourceEvent<Http.SchemaNameCollisions.EnvelopeOne.EnvelopeMatch>), TypeInfoPropertyName = "ResourceEventOfEnvelopeOneEnvelopeMatch")]
[JsonSerializable(typeof(Blueprint.Http.ResourceEvent<Http.SchemaNameCollisions.EnvelopeTwo.EnvelopeMatch>), TypeInfoPropertyName = "ResourceEventOfEnvelopeTwoEnvelopeMatch")]
[JsonSerializable(typeof(Http.SchemaNameCollisions.EnvelopeThree.EnvelopeMatch), TypeInfoPropertyName = "EnvelopeThreeEnvelopeMatch")]
[JsonSerializable(typeof(Http.LegacyEventWidgetApiResource))]
[JsonSerializable(typeof(Http.TypedIdWidgetApiResource))]
[JsonSerializable(typeof(Http.RequiredShapeApiResource))]
[JsonSerializable(typeof(Http.ConverterWrittenBodyId))]
[JsonSerializable(typeof(Http.HrefWidgetApiResource))]
[JsonSerializable(typeof(Http.HrefWidgetSecretPartialApiResource))]
[JsonSerializable(typeof(Http.HrefUnlinkedApiResource))]
[JsonSerializable(typeof(Http.DictionaryShapeApiResource))]
[JsonSerializable(typeof(Http.ExtensionShapeApiResource))]
[JsonSerializable(typeof(Http.ParameterTitlesApiResource))]
[JsonSerializable(typeof(Http.XmlDocShape))]
[JsonSerializable(typeof(Http.NativeEventGadget))]
[JsonSerializable(typeof(Http.MinLengthShapeApiResource))]
[JsonSerializable(typeof(Http.LinkWireWidgetApiResource))]
[JsonSerializable(typeof(ListApiResource<Http.LinkWireWidgetApiResource>))]
[JsonSerializable(typeof(Http.RelLessLinkWireWidgetApiResource))]
public partial class IntegrationTestAppJsonContext : JsonSerializerContext;
