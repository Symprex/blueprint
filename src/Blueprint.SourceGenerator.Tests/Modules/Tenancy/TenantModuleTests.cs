using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.Tenancy;

/// <summary>
/// Proves the <c>TenantModule</c> generates compile-time tenant resolution code
/// that sets <c>ITenantAccessor.Current</c> from <c>ITenantResolver</c> and
/// directly assigns the <c>TenantId</c> property without reflection when present.
/// </summary>
public class TenantModuleTests
{
    [Fact]
    public void When_operation_has_TenantId_property_then_pipeline_assigns_directly()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class TenantCommand : ICommand<string>
                {
                    public string TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(TenantCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().NotBeEmpty();

        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Uses cached services from base class (no per-request DI lookup)
        pipelineSource.Should().Contain("ResolveTenant(this.TenantResolver, this.TenantAccessor, context.UserPrincipal)");

        // Direct property assignment from cached accessor (no reflection)
        pipelineSource.Should().Contain("operation.TenantId = this.TenantAccessor.Current");
    }

    [Fact]
    public void When_operation_has_no_TenantId_property_then_pipeline_only_sets_accessor()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class NoTenantQuery : IQuery<string>
                {
                    public string Filter { get; set; }

                    public static Task<string> HandleAsync(NoTenantQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().NotBeEmpty();

        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Still resolves and sets accessor (all operations get tenant context)
        pipelineSource.Should().Contain("ResolveTenant(");

        // Should NOT attempt direct property assignment
        pipelineSource.Should().NotContain("operation.TenantId");
    }

    [Fact]
    public void When_TenantId_is_inherited_from_base_then_pipeline_still_assigns()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public abstract class TenantAwareBase
                {
                    public string TenantId { get; set; }
                }

                public partial class InheritedTenantCommand : TenantAwareBase, ICommand<string>
                {
                    public string Data { get; set; }

                    public static Task<string> HandleAsync(InheritedTenantCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Should detect TenantId from the base class and assign directly
        pipelineSource.Should().Contain("operation.TenantId = this.TenantAccessor.Current");
    }

    [Fact]
    public void When_TenantId_is_readonly_then_pipeline_skips_assignment()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class ReadonlyTenantQuery : IQuery<string>
                {
                    public string TenantId { get; }

                    public static Task<string> HandleAsync(ReadonlyTenantQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Still resolves and sets accessor
        pipelineSource.Should().Contain("ResolveTenant(");

        // Should NOT assign to readonly property
        pipelineSource.Should().NotContain("operation.TenantId");
    }

    [Fact]
    public void When_TenantId_is_parsable_struct_then_pipeline_uses_TryParse()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                /// <summary>Mirrors S365's ApiId — a parsable struct used for tenant IDs.</summary>
                public readonly struct ApiId : IParsable<ApiId>
                {
                    public string Value { get; }
                    public ApiId(string value) => this.Value = value;
                    public static ApiId Parse(string s, IFormatProvider? provider) => new(s);
                    public static bool TryParse(string? s, IFormatProvider? provider, out ApiId result)
                    {
                        if (s is null) { result = default; return false; }
                        result = new ApiId(s);
                        return true;
                    }
                    public override string ToString() => this.Value;
                }

                /// <summary>
                /// Operation with ApiId TenantId NOT in route — TenantModule should
                /// auto-populate from the accessor using TryParse.
                /// </summary>
                public partial class ParsableTenantCommand : ICommand<string>
                {
                    public ApiId TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(ParsableTenantCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Uses EqualityComparer<T>.Default for value-type default check
        pipelineSource.Should().Contain("EqualityComparer<global::TestNamespace.ApiId>.Default.Equals(operation.TenantId, default)");

        // Uses TryParse with InvariantCulture IFormatProvider (IParsable pattern)
        pipelineSource.Should().Contain("global::TestNamespace.ApiId.TryParse(this.TenantAccessor.Current, System.Globalization.CultureInfo.InvariantCulture, out var _parsedTenantId)");

        // Assigns parsed value
        pipelineSource.Should().Contain("operation.TenantId = _parsedTenantId;");
    }

    [Fact]
    public void When_TenantId_is_parsable_struct_inherited_from_base_then_pipeline_uses_TryParse()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public readonly struct ApiId : IParsable<ApiId>
                {
                    public string Value { get; }
                    public ApiId(string value) => this.Value = value;
                    public static ApiId Parse(string s, IFormatProvider? provider) => new(s);
                    public static bool TryParse(string? s, IFormatProvider? provider, out ApiId result)
                    {
                        if (s is null) { result = default; return false; }
                        result = new ApiId(s);
                        return true;
                    }
                    public override string ToString() => this.Value;
                }

                /// <summary>S365-style base class with ApiId TenantId.</summary>
                public abstract class TenantAwareBase
                {
                    public ApiId TenantId { get; set; }
                }

                public partial class InheritedParsableTenantQuery : TenantAwareBase, IQuery<string>
                {
                    public string Filter { get; set; }

                    public static Task<string> HandleAsync(InheritedParsableTenantQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Should detect inherited ApiId TenantId and use TryParse
        pipelineSource.Should().Contain("ApiId.TryParse(this.TenantAccessor.Current, System.Globalization.CultureInfo.InvariantCulture, out var _parsedTenantId)");
        pipelineSource.Should().Contain("operation.TenantId = _parsedTenantId;");
    }

    [Fact]
    public void When_TenantId_is_in_route_then_pipeline_does_not_auto_populate_from_accessor()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public readonly struct ApiId : IParsable<ApiId>
                {
                    public string Value { get; }
                    public ApiId(string value) => this.Value = value;
                    public static ApiId Parse(string s, IFormatProvider? provider) => new(s);
                    public static bool TryParse(string? s, IFormatProvider? provider, out ApiId result)
                    {
                        if (s is null) { result = default; return false; }
                        result = new ApiId(s);
                        return true;
                    }
                    public override string ToString() => this.Value;
                }

                /// <summary>
                /// TenantId IS in the route — PropertyOwnershipModule handles route binding,
                /// TenantModule should NOT auto-populate from accessor.
                /// </summary>
                [RootLink("/tenants/{TenantId}/items")]
                [HttpGet]
                public partial class RouteBoundTenantQuery : IQuery<string>
                {
                    public ApiId TenantId { get; set; }
                    public string Filter { get; set; }

                    public static Task<string> HandleAsync(RouteBoundTenantQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Still resolves tenant accessor (all operations get tenant context)
        pipelineSource.Should().Contain("ResolveTenant(");

        // Should NOT auto-populate TenantId from accessor — route binding handles it
        pipelineSource.Should().NotContain("_parsedTenantId");
        pipelineSource.Should().NotContain("operation.TenantId = this.TenantAccessor.Current");
    }

    [Fact]
    public void When_TenantId_is_in_a_legacy_alternate_named_route_placeholder_then_pipeline_does_not_auto_populate_from_accessor()
    {
        // Legacy routed {TenantId:Id} as {TenantId} and bound TenantId from it as it bound {TenantId} (Signature365
        // 8b40ecc961, Blueprint/ApiOperationLink.cs:53-78; HttpRouteMessagePopulationSource.cs:36-41), so the two are the
        // same to tenant filling: the route supplies it. Signature365's GetDefaultProductForTenantQuery has this shape.
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public readonly struct ApiId : IParsable<ApiId>
                {
                    public string Value { get; }
                    public ApiId(string value) => this.Value = value;
                    public static ApiId Parse(string s, IFormatProvider? provider) => new(s);
                    public static bool TryParse(string? s, IFormatProvider? provider, out ApiId result)
                    {
                        if (s is null) { result = default; return false; }
                        result = new ApiId(s);
                        return true;
                    }
                    public override string ToString() => this.Value;
                }

                public class TenantResource : ApiResource
                {
                    public override string Object => "tenant";
                    public string Id { get; set; }
                }

                [Link(typeof(TenantResource), "/billing/tenants/{TenantId:Id}/products/default", Rel = "defaultProduct")]
                [HttpGet]
                public partial class GetDefaultProductForTenantQuery : IQuery<string>
                {
                    public ApiId TenantId { get; set; }

                    public static Task<string> HandleAsync(GetDefaultProductForTenantQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = string.Join("\n", result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator)).Select(static s => s.SourceText));

        pipelineSource.Should().Contain("ResolveTenant(");
        pipelineSource.Should().NotContain("_parsedTenantId");
    }

    [Fact]
    public void When_TenantId_is_nullable_value_type_then_pipeline_uses_HasValue_guard()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public readonly struct ApiId : IParsable<ApiId>
                {
                    public string Value { get; }
                    public ApiId(string value) => this.Value = value;
                    public static ApiId Parse(string s, IFormatProvider? provider) => new(s);
                    public static bool TryParse(string? s, IFormatProvider? provider, out ApiId result)
                    {
                        if (s is null) { result = default; return false; }
                        result = new ApiId(s);
                        return true;
                    }
                    public override string ToString() => this.Value;
                }

                [RootLink("/items")]
                [HttpGet]
                public partial class GetItemsQuery : IQuery<string>
                {
                    public ApiId? TenantId { get; set; }
                    public string Filter { get; set; }

                    public static Task<string> HandleAsync(GetItemsQuery operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Should use HasValue guard for Nullable<ApiId>
        pipelineSource.Should().Contain("!operation.TenantId.HasValue");

        // Should TryParse the unwrapped type
        pipelineSource.Should().Contain("ApiId.TryParse(this.TenantAccessor.Current, System.Globalization.CultureInfo.InvariantCulture, out var _parsedTenantId)");
        pipelineSource.Should().Contain("operation.TenantId = _parsedTenantId;");
    }

    [Fact]
    public void When_TenantId_not_in_route_then_descriptor_binds_it_from_the_body()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items")]
                public partial class CreateItemCommand : ICommand<string>
                {
                    public string TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(CreateItemCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Legacy bound a command's TenantId from its body like any other property, and SetTenantMiddleware filled it only
        // when that left it unset (Signature365 8b40ecc961, Common/Api/SetTenantMiddleware.cs:60-68).
        descriptorSource.Should().Contain("new global::Blueprint.Model.OperationPropertyBindingEntry(\"TenantId\", global::Blueprint.Model.PropertyBindingSource.Body, \"tenantId\"");
        descriptorSource.Should().NotContain("PropertyBindingSource.AuthContext");
    }

    [Fact]
    public void When_TenantId_in_route_then_descriptor_uses_route_not_auth_context()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/tenants/{TenantId}/items")]
                public partial class CreateItemCommand : ICommand<string>
                {
                    public string TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(CreateItemCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // TenantId in route should be Route-bound, not AuthContext
        descriptorSource.Should().Contain("PropertyBindingSource.Route");
        descriptorSource.Should().NotContain("PropertyBindingSource.AuthContext");
    }

    [Fact]
    public void When_TenantId_has_explicit_FromBody_then_descriptor_uses_body_not_auth_context()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items")]
                public partial class CreateItemCommand : ICommand<string>
                {
                    [FromBody]
                    public string TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(CreateItemCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Explicit [FromBody] should override tenant convention
        descriptorSource.Should().Contain("PropertyBindingSource.Body");
        descriptorSource.Should().NotContain("PropertyBindingSource.AuthContext");
    }

    [Fact]
    public void When_TenantId_has_explicit_FromBody_then_pipeline_does_not_auto_populate()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [RootLink("/items")]
                public partial class CreateItemCommand : ICommand<string>
                {
                    [FromBody]
                    public string TenantId { get; set; }
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(CreateItemCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Tenant resolution should still happen (sets accessor)
        pipelineSource.Should().Contain("ResolveTenant(");

        // But should NOT auto-assign to operation.TenantId (explicit binding handles that)
        pipelineSource.Should().NotContain("operation.TenantId = this.TenantAccessor.Current");
    }

    // Legacy's SetTenantMiddleware (Signature365 8b40ecc961, Common/Api/SetTenantMiddleware.cs:63-70) filled only a TenantId
    // whose type was ApiId or ApiId?, that CanWrite and that was not init-only (:77-90). An app declares that type with
    // [BlueprintAppTenancy(TenantIdType = ...)]; one that does not keeps every supported kind filled.

    [Fact]
    public void When_app_declares_TenantIdType_and_TenantId_is_that_type_then_pipeline_fills_it()
    {
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy(TenantIdType = typeof(ApiId))]",
            "public ApiId TenantId { get; set; }");

        // Legacy's `x != null ? x : ApiId.Hydrate(current)` bound ApiId.operator !=(ApiId, string), so an unset
        // non-nullable ApiId was filled too.
        pipelineSource.Should().Contain("EqualityComparer<global::TestNamespace.ApiId>.Default.Equals(operation.TenantId, default)");
        pipelineSource.Should().Contain("operation.TenantId = (global::TestNamespace.ApiId)System.ComponentModel.TypeDescriptor.GetConverter(typeof(global::TestNamespace.ApiId)).ConvertFromInvariantString(this.TenantAccessor.Current!)!;");
    }

    [Fact]
    public void When_app_declares_TenantIdType_and_TenantId_is_its_nullable_then_pipeline_fills_it()
    {
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy(TenantIdType = typeof(ApiId))]",
            "public ApiId? TenantId { get; set; }");

        pipelineSource.Should().Contain("!operation.TenantId.HasValue");
        pipelineSource.Should().Contain("ConvertFromInvariantString(this.TenantAccessor.Current!)");
    }

    [Fact]
    public void When_app_declares_TenantIdType_as_a_nullable_then_TenantId_of_the_underlying_type_is_filled()
    {
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy(TenantIdType = typeof(ApiId?))]",
            "public ApiId TenantId { get; set; }");

        pipelineSource.Should().Contain("ConvertFromInvariantString(this.TenantAccessor.Current!)");
    }

    [Fact]
    public void When_app_declares_TenantIdType_and_TenantId_is_a_string_then_pipeline_does_not_fill_it()
    {
        // Signature365's GenerateSignatureCommand, its IdentityServer TenantUsers operations and its AI analytics
        // operations have a string TenantId, which legacy left null when the request did.
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy(TenantIdType = typeof(ApiId))]",
            "public string TenantId { get; set; }");

        ShouldNotFillTenantId(pipelineSource);
    }

    [Fact]
    public void When_app_declares_TenantIdType_and_TenantId_is_another_parsable_type_then_pipeline_does_not_fill_it()
    {
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy(TenantIdType = typeof(ApiId))]",
            "public OtherId TenantId { get; set; }");

        ShouldNotFillTenantId(pipelineSource);
    }

    [Fact]
    public void When_app_declares_BlueprintAppTenancy_without_a_TenantIdType_then_pipeline_fills_a_string_TenantId()
    {
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy]",
            "public string TenantId { get; set; }");

        pipelineSource.Should().Contain("operation.TenantId = this.TenantAccessor.Current;");
    }

    [Fact]
    public void When_app_does_not_declare_TenantIdType_and_TenantId_has_a_type_converter_then_pipeline_fills_it()
    {
        var pipelineSource = GeneratePipeline(
            string.Empty,
            "public ApiId TenantId { get; set; }");

        pipelineSource.Should().Contain("ConvertFromInvariantString(this.TenantAccessor.Current!)");
    }

    [Fact]
    public void When_TenantId_is_init_only_then_pipeline_does_not_fill_it()
    {
        // Legacy skipped an init-only TenantId whatever its type (SetTenantMiddleware.cs:65, :77-90).
        var pipelineSource = GeneratePipeline(
            string.Empty,
            "public string TenantId { get; init; }");

        ShouldNotFillTenantId(pipelineSource);
    }

    [Fact]
    public void When_app_declares_TenantIdType_and_TenantId_of_that_type_is_init_only_then_pipeline_does_not_fill_it()
    {
        var pipelineSource = GeneratePipeline(
            "[BlueprintAppTenancy(TenantIdType = typeof(ApiId))]",
            "public ApiId TenantId { get; init; }");

        ShouldNotFillTenantId(pipelineSource);
    }

    [Fact]
    public void When_TenantId_has_a_private_setter_then_pipeline_does_not_fill_it()
    {
        // Legacy's CanWrite held for a private setter, but its generated assignment could not have compiled against one.
        var pipelineSource = GeneratePipeline(
            string.Empty,
            "public string TenantId { get; private set; }");

        ShouldNotFillTenantId(pipelineSource);
    }

    /// <summary>
    /// Generates the pipeline of a <c>TenantCommand</c> declaring <paramref name="tenantIdProperty"/>, hosted by an app
    /// carrying <paramref name="appAttributes"/>, beside an <c>ApiId</c> shaped as Signature365's (a struct with only a
    /// type converter) and an <c>OtherId</c> that is <c>IParsable</c>.
    /// </summary>
    private static string GeneratePipeline(string appAttributes, string tenantIdProperty)
    {
        var source = $$"""
            using Blueprint;
            using Blueprint.Model;
            using System;
            using System.ComponentModel;
            using System.Globalization;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                {{appAttributes}}
                public partial class TestApp : BlueprintApp { }

                [TypeConverter(typeof(ApiIdConverter))]
                public readonly struct ApiId
                {
                    public ApiId(string value) => this.Value = value;
                    public string Value { get; }
                }

                public sealed class ApiIdConverter : TypeConverter
                {
                    public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) => new ApiId((string)value);
                }

                public readonly struct OtherId : IParsable<OtherId>
                {
                    public OtherId(string value) => this.Value = value;
                    public string Value { get; }
                    public static OtherId Parse(string s, IFormatProvider provider) => new(s);
                    public static bool TryParse(string s, IFormatProvider provider, out OtherId result)
                    {
                        result = new OtherId(s);
                        return s is not null;
                    }
                }

                public partial class TenantCommand : ICommand<string>
                {
                    {{tenantIdProperty}}
                    public string Name { get; set; }

                    public static Task<string> HandleAsync(TenantCommand operation)
                    {
                        return Task.FromResult("ok");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSource = string.Join("\n", result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator)).Select(static s => s.SourceText));
        pipelineSource.Should().Contain("class TenantCommandExecutorPipeline");

        return pipelineSource;
    }

    /// <summary>
    /// Asserts that the pipeline still resolves the tenant but fills <c>TenantId</c> by none of the three ways
    /// <c>TenantModule</c> can: a string assignment, a <c>TryParse</c> or a type converter.
    /// </summary>
    private static void ShouldNotFillTenantId(string pipelineSource)
    {
        pipelineSource.Should().Contain("ResolveTenant(this.TenantResolver, this.TenantAccessor, context.UserPrincipal)");
        pipelineSource.Should().NotContain("operation.TenantId = this.TenantAccessor.Current");
        pipelineSource.Should().NotContain("_parsedTenantId");
        pipelineSource.Should().NotContain("ConvertFromInvariantString(this.TenantAccessor.Current");
    }
}
