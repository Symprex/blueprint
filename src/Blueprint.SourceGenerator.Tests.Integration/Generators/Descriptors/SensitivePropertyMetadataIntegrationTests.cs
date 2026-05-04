using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Descriptors;

/// <summary>
/// Proves that the generated descriptor for an operation with <c>[Sensitive]</c>-annotated
/// properties exposes <see cref="SensitivePropertyMetadata"/> via feature data at runtime,
/// and that operations without sensitive properties have no such metadata.
/// </summary>
public class SensitivePropertyMetadataIntegrationTests
{
    [Fact]
    public void Descriptor_exposes_SensitivePropertyMetadata_for_operation_with_sensitive_properties()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(SensitiveFieldsQuery));
        descriptor.Should().NotBeNull();

        var hasMeta = descriptor!.TryGetFeatureData<SensitivePropertyMetadata>(out var meta);
        hasMeta.Should().BeTrue("operation has [Sensitive] properties");
        meta!.PropertyNames.Should().ContainInOrder("Password", "SocialSecurityNumber");
    }

    [Fact]
    public void Descriptor_does_not_expose_SensitivePropertyMetadata_when_no_sensitive_properties()
    {
        var app = new IntegrationTestBlueprintApp();

        var descriptor = app.Operations.FirstOrDefault(static d => d.OperationType == typeof(NonSensitiveQuery));
        descriptor.Should().NotBeNull();

        var hasMeta = descriptor!.TryGetFeatureData<SensitivePropertyMetadata>(out _);
        hasMeta.Should().BeFalse("operation has no [Sensitive] properties");
    }
}

/// <summary>
/// Query with properties marked <c>[Sensitive]</c> for integration testing.
/// </summary>
public partial class SensitiveFieldsQuery : IQuery<string>
{
    /// <summary>Gets or sets the user name (not sensitive).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password (sensitive).</summary>
    [Sensitive]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the SSN (sensitive).</summary>
    [Sensitive]
    public string SocialSecurityNumber { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query to handle.</param>
    /// <returns>A test result string.</returns>
    public static Task<string> HandleAsync(SensitiveFieldsQuery query)
    {
        return Task.FromResult("sensitive-test");
    }
}

/// <summary>
/// Query with no sensitive properties for negative-path integration testing.
/// </summary>
public partial class NonSensitiveQuery : IQuery<string>
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query to handle.</param>
    /// <returns>A test result string.</returns>
    public static Task<string> HandleAsync(NonSensitiveQuery query)
    {
        return Task.FromResult("non-sensitive-test");
    }
}
