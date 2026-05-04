using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.Auditing;

/// <summary>
/// Tests that the sensitive-property-metadata module correctly discovers <c>[Sensitive]</c>
/// attributes on operation properties and emits <c>SetFeatureData&lt;SensitivePropertyMetadata&gt;</c>
/// in the generated descriptor constructor.
/// </summary>
public class SensitivePropertyMetadataModuleTests
{
    [Fact]
    public void When_operation_has_sensitive_properties_then_descriptor_includes_SetFeatureData()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Auditing;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class CreateUserCommand : ICommand<string>
                             {
                                 public string? Name { get; set; }

                                 [Sensitive]
                                 public string? Email { get; set; }

                                 [Sensitive]
                                 public string? Password { get; set; }

                                 public static Task<string> HandleAsync(CreateUserCommand command)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Single(static tree => tree.FilePath.EndsWith("CreateUserCommand_Descriptor.g.cs", System.StringComparison.Ordinal))
            .ToString();

        descriptorSource.Should().Contain("SetFeatureData");
        descriptorSource.Should().Contain("SensitivePropertyMetadata");
        descriptorSource.Should().Contain("\"Email\"");
        descriptorSource.Should().Contain("\"Password\"");
        descriptorSource.Should().NotContain("\"Name\"");
    }

    [Fact]
    public void When_operation_has_no_sensitive_properties_then_descriptor_does_not_include_SetFeatureData()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class GetOrderQuery : IQuery<string>
                             {
                                 public string? OrderId { get; set; }

                                 public static Task<string> HandleAsync(GetOrderQuery query)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Single(static tree => tree.FilePath.EndsWith("GetOrderQuery_Descriptor.g.cs", System.StringComparison.Ordinal))
            .ToString();

        descriptorSource.Should().NotContain("SensitivePropertyMetadata");
    }

    [Fact]
    public void When_operation_has_sensitive_properties_then_metadata_preserves_declaration_order()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Auditing;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class UpdateProfileCommand : ICommand<string>
                             {
                                 [Sensitive]
                                 public string? PhoneNumber { get; set; }

                                 public string? DisplayName { get; set; }

                                 [Sensitive]
                                 public string? SocialSecurityNumber { get; set; }

                                 public static Task<string> HandleAsync(UpdateProfileCommand command)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSource = result.Output
            .Single(static tree => tree.FilePath.EndsWith("UpdateProfileCommand_Descriptor.g.cs", System.StringComparison.Ordinal))
            .ToString();

        // PhoneNumber should appear before SocialSecurityNumber (declaration order)
        var phoneIdx = descriptorSource.IndexOf("\"PhoneNumber\"", System.StringComparison.Ordinal);
        var ssnIdx = descriptorSource.IndexOf("\"SocialSecurityNumber\"", System.StringComparison.Ordinal);

        phoneIdx.Should().BeGreaterThan(-1);
        ssnIdx.Should().BeGreaterThan(phoneIdx, "properties should appear in declaration order");
    }

    [Fact]
    public void When_sensitive_on_non_public_property_then_reports_BPA001_diagnostic()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Auditing;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class SecretCommand : ICommand<string>
                             {
                                 public string? Name { get; set; }

                                 [Sensitive]
                                 private string? InternalToken { get; set; }

                                 public static Task<string> HandleAsync(SecretCommand command)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        result.Diagnostics.Should().ContainSingle()
            .Which.Id.Should().Be("BPA001");
    }

    [Fact]
    public void When_sensitive_on_non_public_property_then_BPA001_includes_type_and_property_name()
    {
        const string source = """
                             using Blueprint;
                             using Blueprint.Auditing;
                             using Blueprint.Model;
                             using System.Threading.Tasks;

                             namespace TestNamespace;

                             [BlueprintAppScan]
                             public partial class TestApp : BlueprintApp
                             {
                             }

                             public partial class TokenCommand : ICommand<string>
                             {
                                 [Sensitive]
                                 internal string? ApiKey { get; set; }

                                 public static Task<string> HandleAsync(TokenCommand command)
                                 {
                                     return Task.FromResult("ok");
                                 }
                             }
                             """;

        var result = TestHelpers.Compile<OperationDescriptorSourceGenerator>(source);

        var diagnostic = result.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("BPA001");

        var formatted = diagnostic.GetMessage();
        formatted.Should().Contain("TokenCommand");
        formatted.Should().Contain("ApiKey");
    }
}
