using Blueprint.SourceGenerator.Generators.ApiResources;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.ApiResource;

/// <summary>
/// Proves that the generated <c>Object</c> override names a resource by the rule legacy Blueprint named it by.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>origin/master</c>, <c>Blueprint.Http/ApiResource.cs:28-31</c>, <c>:68-77</c>) set <c>$object</c> to
/// <c>GetTypeName(GetType())</c>: <c>Replace("ApiResource")</c>, <c>Replace("Resource")</c>, <c>Replace("DTO")</c>, each
/// wherever it occurs, then <c>Camelize</c>, which raises the character at the start and after each underscore, removing
/// the underscore, and lowers the first character only. <c>ApiResource.GetTypeName</c> is that rule (R167), so the
/// override must be too, or a resource's <c>$object</c> and its events' <c>eventId</c> and <c>resourceObject</c> disagree.
/// An explicit name of a legacy resource, such as Signature365's <c>GatewayImportRunApiResource</c>
/// (<c>[JsonSchema("ImportRun")]</c>, <c>this.Object = ....Name.Camelize()</c>), was camelized the same way.
/// </remarks>
public class ApiResourceObjectNameTests
{
    [Theory]
    [InlineData("SMSMessageApiResource", "sMSMessage")]
    [InlineData("APIKeyResource", "aPIKey")]
    [InlineData("FooApiResourceBar", "fooBar")]
    [InlineData("FooResourceBarDTOBaz", "fooBarBaz")]
    [InlineData("Foo_barApiResource", "fooBar")]
    [InlineData("FooDto", "fooDto")]
    [InlineData("TenantApiResource", "tenant")]
    [InlineData("DTO", "dTO")]
    public void A_resource_without_an_explicit_name_is_named_as_legacy_named_it(string className, string expected)
    {
        var result = Compile($$"""
                             public partial class {{className}} : ApiResource
                             {
                             }
                             """);

        Generated(result).Should().Contain($"public override string Object => \"{expected}\";");
    }

    [Fact]
    public void A_collection_resource_keeps_the_list_prefix_on_the_legacy_name()
    {
        var result = Compile("""
                             public partial class SMSMessageListApiResource : ApiResource, IApiResourceCollection
                             {
                                public System.Collections.Generic.IEnumerable<object> GetEnumerable()
                                {
                                    throw new System.NotImplementedException();
                                }
                             }
                             """);

        Generated(result).Should().Contain("public override string Object => \"list.sMSMessageList\";");
    }

    [Fact]
    public void A_JsonSchema_name_is_camelized_as_legacy_camelized_it()
    {
        var result = TestHelpers.Compile<ApiResourceSourceGenerator>(
            """
            using Blueprint.Model;

            namespace NJsonSchema.Annotations
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public class JsonSchemaAttribute : System.Attribute
                {
                    public JsonSchemaAttribute(string name) { Name = name; }
                    public string Name { get; }
                }
            }

            namespace TestNamespace
            {
                [NJsonSchema.Annotations.JsonSchema("SMSMessage")]
                public partial class GatewaySmsApiResource : ApiResource
                {
                }
            }
            """);

        Generated(result).Should().Contain("public override string Object => \"sMSMessage\";");
    }

    [Fact]
    public void A_ResourceObjectName_is_used_as_written()
    {
        var result = Compile("""
                             [Blueprint.Model.Attributes.ResourceObjectName("SMSMessage")]
                             public partial class GatewaySmsApiResource : ApiResource
                             {
                             }
                             """);

        Generated(result).Should().Contain("public override string Object => \"SMSMessage\";");
    }

    private static string Generated(GenerationResult result)
    {
        return string.Join("\n", result.Output.Select(static t => t.ToString()));
    }

    private static GenerationResult Compile(string source)
    {
        return TestHelpers.Compile<ApiResourceSourceGenerator>("using Blueprint.Model;\n\nnamespace TestNamespace;\n\n" + source);
    }
}
