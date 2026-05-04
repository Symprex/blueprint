using Blueprint.SourceGenerator.Generators.ResourceEvents;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.ResourceEvents;

/// <summary>
/// Tests for <see cref="ResourceEventFamilySourceGenerator"/> covering body-bearing events, key-only events,
/// diagnostics for invalid shapes, and generated implicit conversion operators.
/// </summary>
public class ResourceEventFamilySourceGeneratorTests
{
    private const string SnapshotsFolder = "Generators/ResourceEvents/Snapshots";

    [Fact]
    public async Task When_body_bearing_event_with_single_resource_parameter_then_generates_implicit_conversion()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Created, "created")]
                public sealed partial record Created(TestApiResource Resource);

                [ResourceEventReason(ChangeType.Updated, "updated")]
                public sealed partial record Updated(TestApiResource Resource);
            }
            """);

        await result.VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_key_only_event_then_generates_key_only_result_conversion()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Deleted, "deleted")]
                public sealed partial record Deleted([ResourceEventKey] string Key);
            }
            """);

        await result.VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public async Task When_explicit_body_and_key_attributes_then_generates_partial_body_event()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            public class PartialBodyResource
            {
                public string Value { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Updated, "partial.updated")]
                public sealed partial record PartialUpdated(
                    [ResourceEventBody] PartialBodyResource Body,
                    [ResourceEventKey] string Key);
            }
            """);

        await result.VerifySuccessAsync(SnapshotsFolder);
    }

    [Fact]
    public void When_family_not_partial_then_reports_BPRE001()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Created, "created")]
                public sealed partial record Created(TestApiResource Resource);
            }
            """);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "BPRE001");
    }

    [Fact]
    public void When_record_not_partial_then_reports_BPRE002()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Created, "created")]
                public sealed record Created(TestApiResource Resource);
            }
            """);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "BPRE002");
    }

    [Fact]
    public void When_ambiguous_shape_then_reports_BPRE003()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            public class OtherType { }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Updated, "updated")]
                public sealed partial record Ambiguous(TestApiResource Resource, string ExtraParam);
            }
            """);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "BPRE003");
    }

    [Fact]
    public void When_duplicate_reason_then_reports_BPRE004()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource), ResourceObject = "test")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Created, "created")]
                public sealed partial record Created(TestApiResource Resource);

                [ResourceEventReason(ChangeType.Updated, "created")]
                public sealed partial record AlsoCreated(TestApiResource Resource);
            }
            """);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "BPRE004");
    }

    [Fact]
    public void When_missing_resource_object_then_reports_BPRE005()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class TestApiResource : ApiResource
            {
                public override string Object => "test";
                public string Id { get; set; } = "";
            }

            [ResourceEventFamily(typeof(TestApiResource))]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Created, "created")]
                public sealed partial record Created(TestApiResource Resource);
            }
            """);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "BPRE005");
    }

    [Fact]
    public void When_resource_type_missing_id_property_then_reports_BPRE006()
    {
        var result = Compile("""
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class NoIdResource : ApiResource
            {
                public override string Object => "noId";
                public string Name { get; set; } = "";
            }

            [ResourceEventFamily(typeof(NoIdResource), ResourceObject = "noId")]
            public static partial class TestResourceEvents
            {
                [ResourceEventReason(ChangeType.Created, "created")]
                public sealed partial record Created(NoIdResource Resource);
            }
            """);

        result.Diagnostics.Should().ContainSingle(d => d.Id == "BPRE006");
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<ResourceEventFamilySourceGenerator>(source, assertCacheability: false);
    }
}
