using Blueprint.SourceGenerator.Modules.Validation;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.Validation;

/// <summary>
/// Tests that <see cref="ValidationManifestModule"/> contributes per-operation validation plan
/// metadata (<c>validation.enabled</c>, <c>validation.mode</c>, <c>validation.properties</c>)
/// to the shared debug manifest.
/// </summary>
public class ValidationManifestModuleTests
{
    [Fact]
    public void When_operation_has_no_validation_attributes_then_manifest_shows_disabled()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class PlainQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ValidationManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.PlainQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("validation.enabled");
        result.Value.Entries["validation.enabled"].ToString().Should().Be("False");
        result.Value.Entries["validation.mode"].ToString().Should().Be("none");
        result.Value.Entries.Should().NotContainKey("validation.properties");
    }

    [Fact]
    public void When_operation_has_Required_attribute_then_manifest_shows_per_property_fast_path()
    {
        var compilation = CreateCompilation(
            """
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class CreateItemCommand : ICommand<string>
            {
                [Required]
                public string Name { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new ValidationManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.CreateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["validation.enabled"].ToString().Should().Be("True");
        result.Value.Entries["validation.mode"].ToString().Should().Be("per-property");
        result.Value.Entries.Should().ContainKey("validation.properties");

        var properties = result.Value.Entries["validation.properties"] as JArray;
        properties.Should().NotBeNull();
        properties!.Count.Should().Be(1);

        var prop = properties[0] as JObject;
        prop.Should().NotBeNull();
        prop!["name"]!.ToString().Should().Be("Name");
        prop["strategy"]!.ToString().Should().Be("fast-path");
        prop["rules"]!.Should().BeOfType<JArray>();
        ((JArray)prop["rules"]!).Should().Contain(t => t.ToString() == "Required");
    }

    [Fact]
    public void When_operation_has_IValidatableObject_then_manifest_shows_whole_object_fallback()
    {
        var compilation = CreateCompilation(
            """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class ComplexCommand : ICommand<string>, IValidatableObject
            {
                public string Name { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new ValidationManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.ComplexCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["validation.enabled"].ToString().Should().Be("True");
        result.Value.Entries["validation.mode"].ToString().Should().Be("per-property+ivalidatable",
            "IValidatableObject-only types should use hybrid mode: property checks then IValidatableObject.Validate()");
        result.Value.Entries.Should().NotContainKey("validation.properties",
            "with no property-level validation attributes, the properties list should be omitted");
    }

    [Fact]
    public void When_operation_has_multiple_validated_properties_then_manifest_lists_all()
    {
        var compilation = CreateCompilation(
            """
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class UpdateItemCommand : ICommand<string>
            {
                [Required]
                [MaxLength(100)]
                public string Name { get; set; }

                [Required]
                public string Description { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new ValidationManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.UpdateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["validation.mode"].ToString().Should().Be("per-property");

        var properties = result.Value.Entries["validation.properties"] as JArray;
        properties.Should().NotBeNull();
        properties!.Count.Should().Be(2);

        var nameProp = properties[0] as JObject;
        nameProp!["name"]!.ToString().Should().Be("Name");
        nameProp["strategy"]!.ToString().Should().Be("fast-path");
        var nameRules = (JArray)nameProp["rules"]!;
        nameRules.Should().Contain(t => t.ToString() == "Required");
        nameRules.Should().Contain(t => t.ToString() == "MaxLength");

        var descProp = properties[1] as JObject;
        descProp!["name"]!.ToString().Should().Be("Description");
        descProp["strategy"]!.ToString().Should().Be("fast-path");
    }

    [Fact]
    public void When_property_has_display_name_attribute_then_manifest_shows_runtime_fallback()
    {
        var compilation = CreateCompilation(
            """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class DisplayNameCommand : ICommand<string>
            {
                [Required]
                [DisplayName("Item Name")]
                public string Name { get; set; }
            }
            """);

        var config = new BlueprintSourceGenerationConfig([new ValidationManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.DisplayNameCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["validation.mode"].ToString().Should().Be("per-property");

        var properties = result.Value.Entries["validation.properties"] as JArray;
        properties.Should().NotBeNull();
        properties!.Count.Should().Be(1);

        var prop = properties[0] as JObject;
        prop!["name"]!.ToString().Should().Be("Name");
        prop["strategy"]!.ToString().Should().Be("runtime-fallback");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
