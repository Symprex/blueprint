using Blueprint.SourceGenerator.Modules.Auditing;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.Auditing;

/// <summary>
/// Tests that <see cref="SensitivePropertyManifestModule"/> contributes per-operation sensitive-property
/// metadata (<c>sensitiveProperties.enabled</c>, <c>sensitiveProperties.names</c>) to the shared debug manifest.
/// </summary>
public class SensitivePropertyManifestModuleTests
{
    [Fact]
    public void When_operation_has_no_sensitive_properties_then_manifest_shows_disabled()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class PlainQuery : IQuery<string>
            {
                public string Name { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new SensitivePropertyManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.PlainQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["sensitiveProperties.enabled"].ToString().Should().Be("False");
        result.Value.Entries.Should().NotContainKey("sensitiveProperties.names");
    }

    [Fact]
    public void When_operation_has_sensitive_property_then_manifest_shows_property_name()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Auditing;

            namespace TestNamespace;

            public partial class LoginCommand : ICommand<string>
            {
                public string Username { get; set; }

                [Sensitive]
                public string Password { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new SensitivePropertyManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.LoginCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["sensitiveProperties.enabled"].ToString().Should().Be("True");

        var names = result.Value.Entries["sensitiveProperties.names"] as JArray;
        names.Should().NotBeNull();
        names!.Count.Should().Be(1);
        names[0]!.ToString().Should().Be("Password");
    }

    [Fact]
    public void When_operation_has_multiple_sensitive_properties_then_manifest_lists_all()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Auditing;

            namespace TestNamespace;

            public partial class ResetPasswordCommand : ICommand<string>
            {
                public string UserId { get; set; }

                [Sensitive]
                public string OldPassword { get; set; }

                [Sensitive]
                public string NewPassword { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new SensitivePropertyManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.ResetPasswordCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["sensitiveProperties.enabled"].ToString().Should().Be("True");

        var names = result.Value.Entries["sensitiveProperties.names"] as JArray;
        names.Should().NotBeNull();
        names!.Count.Should().Be(2);

        var nameStrings = names.Select(static n => n.ToString()).ToList();
        nameStrings.Should().Contain("OldPassword");
        nameStrings.Should().Contain("NewPassword");
    }

    [Fact]
    public void When_sensitive_on_non_public_property_then_manifest_skips_it()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Auditing;

            namespace TestNamespace;

            public partial class InternalCommand : ICommand<string>
            {
                [Sensitive]
                internal string Secret { get; set; }

                [Sensitive]
                public string Token { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new SensitivePropertyManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.InternalCommand", compilation);

        result.Value.Should().NotBeNull();
        result.Value!.Entries["sensitiveProperties.enabled"].ToString().Should().Be("True");

        var names = result.Value.Entries["sensitiveProperties.names"] as JArray;
        names.Should().NotBeNull();
        names!.Count.Should().Be(1);
        names[0]!.ToString().Should().Be("Token");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Auditing.SensitiveAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
