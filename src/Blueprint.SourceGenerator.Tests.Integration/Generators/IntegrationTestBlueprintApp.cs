using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

/// <summary>
/// Provides a minimal app shell for integration fixtures that need the generator to discover operations through the same
/// app-scan entry point used by real Blueprint consumers.
/// </summary>
[BlueprintAppScan]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(IntegrationTestAppJsonContext))]
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
[JsonSerializable(typeof(E2EWidgetResponse))]
[JsonSerializable(typeof(E2EJobAcceptedPayload))]
[JsonSerializable(typeof(E2EConflictDetail))]
[JsonSerializable(typeof(ExpandableTestEmployee))]
[JsonSerializable(typeof(AlwaysExpandTestEmployee))]
public partial class IntegrationTestAppJsonContext : JsonSerializerContext;
