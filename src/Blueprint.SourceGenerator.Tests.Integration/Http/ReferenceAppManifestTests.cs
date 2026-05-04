using System.Text;
using Blueprint.Model;
using Blueprint.SourceGenerator.Sample;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Generates the formal Phase 1 review artifact: a complete route/host/auth/result manifest
/// for the reference app. This Verify snapshot is the final Phase 1 exit gate evidence,
/// proving that discovery, pipeline, result model, error model, HTTP host, validation, auth,
/// and observability metadata work together in the generated registration.
/// </summary>
public class ReferenceAppManifestTests
{
    private const string SnapshotsFolder = "Snapshots/";

    [Fact]
    public async Task When_manifest_generated_then_full_surface_matches_reviewed_snapshot_Async()
    {
        var app = new SampleBlueprintApp();
        var manifest = BuildManifest(app);

        await Verify(manifest)
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public void When_manifest_generated_then_every_http_operation_has_method_metadata()
    {
        var app = new SampleBlueprintApp();

        foreach (var descriptor in app.Operations)
        {
            descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methods)
                .Should().BeTrue($"operation {descriptor.Name} should have HTTP method metadata");
            methods!.HttpMethods.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void When_manifest_generated_then_every_http_operation_has_host_exposure()
    {
        var app = new SampleBlueprintApp();

        foreach (var descriptor in app.Operations)
        {
            descriptor.TryGetFeatureData<OperationHostExposureFeatureData>(out var hosts)
                .Should().BeTrue($"operation {descriptor.Name} should have host exposure metadata");
            hosts!.HostNames.Should().Contain("http");
        }
    }

    [Fact]
    public void When_manifest_generated_then_every_operation_has_auth_metadata()
    {
        var app = new SampleBlueprintApp();

        foreach (var descriptor in app.Operations)
        {
            descriptor.TryGetFeatureData<OperationAuthenticationFeatureData>(out _)
                .Should().BeTrue($"operation {descriptor.Name} should have authentication metadata");
        }
    }

    [Fact]
    public void When_manifest_generated_then_every_operation_has_failure_outcomes()
    {
        var app = new SampleBlueprintApp();

        foreach (var descriptor in app.Operations)
        {
            descriptor.TryGetFeatureData<OperationFailureOutcomesFeatureData>(out var failures)
                .Should().BeTrue($"operation {descriptor.Name} should have failure outcome metadata");
            failures!.Outcomes.Should().NotBeEmpty();
        }
    }

    private static string BuildManifest(BlueprintApp app)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Blueprint Reference App — Phase 1 Manifest");
        sb.AppendLine();
        sb.AppendLine("## Operations");
        sb.AppendLine();

        foreach (var descriptor in app.Operations.OrderBy(static d => d.Name))
        {
            sb.AppendLine($"### {descriptor.Name}");
            sb.AppendLine($"  Type: {descriptor.OperationType.Name}");
            sb.AppendLine($"  Source: {descriptor.Source}");

            if (descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methods))
            {
                sb.AppendLine($"  HTTP Methods: {string.Join(", ", methods.HttpMethods)}");
            }

            foreach (var url in descriptor.Urls)
            {
                sb.AppendLine($"  URL: {url.UrlFormat}");
            }

            if (descriptor.TryGetFeatureData<OperationHostExposureFeatureData>(out var hosts))
            {
                sb.AppendLine($"  Hosts: {string.Join(", ", hosts.HostNames)}");
            }

            if (descriptor.TryGetFeatureData<OperationAuthenticationFeatureData>(out var auth))
            {
                sb.AppendLine($"  Auth: {auth.Requirement}");
            }

            if (descriptor.TryGetFeatureData<OperationDiscoveryFeatureData>(out var discovery))
            {
                sb.AppendLine($"  Discovery: {discovery.Kind} ({discovery.Namespace}.{discovery.TypeName})");
            }

            if (descriptor.TryGetFeatureData<OperationGroupMetadataFeatureData>(out var group))
            {
                sb.AppendLine($"  Group: {group.GroupName}");
            }

            if (descriptor.TryGetFeatureData<OperationResultOutcomesFeatureData>(out var results))
            {
                sb.AppendLine("  Result Outcomes:");
                foreach (var outcome in results.Outcomes)
                {
                    sb.AppendLine($"    - {outcome.ResultType.Name}");
                }
            }

            if (descriptor.TryGetFeatureData<OperationFailureOutcomesFeatureData>(out var failures))
            {
                sb.AppendLine("  Failure Outcomes:");
                foreach (var outcome in failures.Outcomes)
                {
                    sb.AppendLine($"    - {outcome.TranslatorId}: {outcome.HandledExceptionTypeName} -> {outcome.ResultTypeName}");
                }
            }

            if (descriptor.TryGetFeatureData<OperationHttpResponseMetadataFeatureData>(out var httpResponses))
            {
                sb.AppendLine("  HTTP Responses:");
                foreach (var response in httpResponses.Responses)
                {
                    sb.AppendLine($"    - {response.StatusCode}: hasBody={response.HasResponseBody} types=[{string.Join(", ", response.BodyTypeNames)}]");
                }
            }

            if (descriptor.Links.Count > 0)
            {
                sb.AppendLine("  Links:");
                foreach (var link in descriptor.Links)
                {
                    sb.AppendLine($"    - rel={link.Rel} url={link.UrlFormat} resource={link.ResourceType?.Name}");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Resource Links");
        sb.AppendLine();
        foreach (var link in app.Links.OrderBy(static l => l.ResourceType?.Name).ThenBy(static l => l.Rel))
        {
            sb.AppendLine($"  {link.ResourceType?.Name ?? "(none)"} [{link.Rel}] -> {link.UrlFormat} ({link.OperationDescriptor.Name})");
        }

        sb.AppendLine();
        sb.AppendLine("## Serializer Context");
        sb.AppendLine($"  Type: {app.AppOwnedSerializerContext?.GetType().Name ?? "(none)"}");

        return sb.ToString();
    }
}
