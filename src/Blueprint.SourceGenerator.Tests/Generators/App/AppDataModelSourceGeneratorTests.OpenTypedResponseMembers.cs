using System.Collections.Immutable;
using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

/// <summary>
/// BP012 and BP013: a member reachable from a declared response root whose static type does not say what it holds.
/// </summary>
/// <remarks>
/// The app's source-generated context writes a declared root alone, so a value of a type it does not register behind
/// such a member failed the response when it was written instead of the build (Symprex/blueprint#94).
/// </remarks>
public class AppDataModelSourceGeneratorTestsOpenTypedResponseMembers
{
    public static TheoryData<string, string, string> OpenShapes => new()
    {
        { "public object? Value { get; set; }", string.Empty, "TestNamespace.Payload.Value" },
        { "public dynamic? Value { get; set; }", string.Empty, "TestNamespace.Payload.Value" },
        { "public IShape? Value { get; set; }", "public interface IShape { string Name { get; } }", "TestNamespace.Payload.Value" },
        { "public ShapeBase? Value { get; set; }", "public abstract class ShapeBase { public string? Name { get; set; } }", "TestNamespace.Payload.Value" },
        { "public Dictionary<string, object?> Value { get; set; } = new();", string.Empty, "TestNamespace.Payload.Value" },
        { "public IReadOnlyDictionary<string, object>? Value { get; set; }", string.Empty, "TestNamespace.Payload.Value" },
        { "public List<object> Value { get; set; } = new();", string.Empty, "TestNamespace.Payload.Value" },
        { "public IEnumerable<IShape>? Value { get; set; }", "public interface IShape { string Name { get; } }", "TestNamespace.Payload.Value" },
        { "public object[] Value { get; set; } = [];", string.Empty, "TestNamespace.Payload.Value" },
        { "public System.Collections.ArrayList? Value { get; set; }", string.Empty, "TestNamespace.Payload.Value" },
        { "public Nested? Value { get; set; }", "public sealed class Nested { public object? Inner { get; set; } }", "TestNamespace.Nested.Inner" },
        { "public List<Nested> Value { get; set; } = new();", "public sealed class Nested { public object? Inner { get; set; } }", "TestNamespace.Nested.Inner" },
        { "public Wrapper<object>? Value { get; set; }", "public sealed class Wrapper<T> { public T? Item { get; set; } }", "TestNamespace.Wrapper<object>.Item" },
        { "public KeyValuePair<object, string> Value { get; set; }", string.Empty, "TestNamespace.Payload.Value" },
        { "[JsonInclude] public object? Value;", string.Empty, "TestNamespace.Payload.Value" },
        { "public object? Value { get; }", string.Empty, "TestNamespace.Payload.Value" },
        { "public ProblemDetails? Value { get; set; }", string.Empty, "Microsoft.AspNetCore.Mvc.ProblemDetails.Extensions" },
        { "[JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public object? Value { get; set; }", string.Empty, "TestNamespace.Payload.Value" },
    };

    public static TheoryData<string, string> ClosedShapes => new()
    {
        { "public string? Value { get; set; }", string.Empty },
        { "public IReadOnlyList<Closed>? Value { get; set; }", "public sealed class Closed { public int Count { get; set; } }" },
        { "public IDictionary<string, string[]>? Value { get; set; }", string.Empty },
        { "public System.Text.Json.JsonElement Value { get; set; }", string.Empty },
        { "public System.Text.Json.Nodes.JsonNode? Value { get; set; }", string.Empty },
        { "[JsonIgnore] public object? Value { get; set; }", string.Empty },
        { "[JsonConverter(typeof(ObjectAsStringConverter))] public object? Value { get; set; }", ObjectAsStringConverter },
        { "public ConvertedShape? Value { get; set; }", "[JsonConverter(typeof(ConvertedShapeConverter))] public abstract class ConvertedShape { } " + ConvertedShapeConverter },
        { "[JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public object? Value { get; set; }", string.Empty },
        { "public Tree? Value { get; set; }", "public sealed class Tree : List<Tree> { }" },
        { "public TreeMap? Value { get; set; }", "public sealed class TreeMap : Dictionary<string, TreeMap> { }" },
        { "public object? Value;", string.Empty },
        { "[ObjectAsString] public object? Value { get; set; }", "public sealed class ObjectAsStringAttribute : JsonConverterAttribute { public override JsonConverter? CreateConverter(Type typeToConvert) => new ObjectAsStringConverter(); } " + ObjectAsStringConverter },
        { "public PolymorphicShape? Value { get; set; }", "[JsonDerivedType(typeof(Circle), \"circle\")] public abstract class PolymorphicShape { } public sealed class Circle : PolymorphicShape { public int Radius { get; set; } }" },
    };

    [Theory]
    [MemberData(nameof(OpenShapes))]
    public void When_a_declared_response_root_reaches_an_open_typed_member_then_generator_reports_BP012(string member, string declarations, string expectedMember)
    {
        var result = Compile(member, declarations);

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain($"'{expectedMember}'").And.Contain("'TestNamespace.Payload'");
    }

    [Fact]
    public void When_the_open_typed_member_is_on_a_base_class_of_the_root_then_generator_reports_BP012()
    {
        var result = Compile(string.Empty, "public abstract class PayloadBase { public object? Inherited { get; set; } }", payloadBase: "PayloadBase");

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'TestNamespace.PayloadBase.Inherited'");
    }

    [Theory]
    [MemberData(nameof(ClosedShapes))]
    public void When_a_declared_response_root_reaches_only_closed_members_then_generator_reports_nothing(string member, string declarations)
    {
        var result = Compile(member, declarations);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_the_app_allows_the_runtime_type_fallback_then_generator_does_not_report_BP012()
    {
        var result = Compile("public object? Value { get; set; }", string.Empty, appArguments: ", AllowRuntimeTypeFallback = true");

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_the_member_declares_registered_runtime_types_then_generator_reports_nothing()
    {
        var result = Compile(
            "[BlueprintRuntimeTypes(typeof(Known), typeof(Known[]))] public Dictionary<string, object?> Value { get; set; } = new();",
            "public sealed class Known { public int Count { get; set; } }",
            extraRegistrations: ["Known", "Known[]"]);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_the_member_declares_an_unregistered_runtime_type_then_generator_reports_BP013_and_not_BP012()
    {
        var result = Compile(
            "[BlueprintRuntimeTypes(typeof(Known), typeof(Unknown))] public object? Value { get; set; }",
            "public sealed class Known { } public sealed class Unknown { }",
            extraRegistrations: ["Known"]);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP013");
        result.Diagnostics.Single().GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'TestNamespace.Payload.Value'").And.Contain("'TestNamespace.Unknown'");
    }

    [Fact]
    public void When_a_declared_runtime_type_itself_reaches_an_open_typed_member_then_generator_reports_BP012()
    {
        var result = Compile(
            "[BlueprintRuntimeTypes(typeof(Known))] public object? Value { get; set; }",
            "public sealed class Known { public object? Loose { get; set; } }",
            extraRegistrations: ["Known"]);

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'TestNamespace.Known.Loose'");
    }

    [Theory]
    [InlineData("object[]")]
    [InlineData("List<object>")]
    public void When_a_declared_runtime_collection_holds_open_elements_then_generator_reports_BP012_on_the_member(string runtimeType)
    {
        var result = Compile(
            $"[BlueprintRuntimeTypes(typeof({runtimeType}))] public object? Value {{ get; set; }}",
            string.Empty,
            extraRegistrations: [runtimeType]);

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'TestNamespace.Payload.Value'");
    }

    [Fact]
    public void When_a_private_member_shadows_an_open_public_base_member_then_generator_still_reports_the_base_member()
    {
        // System.Text.Json writes the public base property; a non-public one hiding it does not take its place.
        var result = Compile("private new string? Value { get; set; }", "public class PayloadBase { public object? Value { get; set; } }", payloadBase: "PayloadBase");

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'TestNamespace.PayloadBase.Value'");
    }

    [Theory]
    [InlineData("public List<object> Value { get; } = new();")]
    [InlineData("public Dictionary<string, object> Value { get; } = new();")]
    public void When_the_context_ignores_read_only_properties_a_getter_only_collection_is_still_reported(string member)
    {
        // System.Text.Json still writes a getter-only collection under IgnoreReadOnlyProperties.
        var result = Compile(member, string.Empty, contextAttributes: "[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]");

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP012");
    }

    [Fact]
    public void When_the_context_ignores_read_only_properties_an_annotated_getter_only_collection_is_still_checked()
    {
        var result = Compile(
            "[BlueprintRuntimeTypes(typeof(Unknown))] public Dictionary<string, object> Value { get; } = new();",
            "public sealed class Unknown { }",
            contextAttributes: "[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]");

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP013");
    }

    [Fact]
    public void When_a_response_type_nests_the_framework_problem_details_then_generator_reports_its_extensions()
    {
        // The referenced ASP.NET Core ProblemDetails, not an in-source substitute, so the walk must reach a framework
        // assembly; only ValidationProblemDetails, from an assembly the harness does not reference, is stubbed.

        var result = Compile("public ProblemDetails? Value { get; set; }", string.Empty, stubProblemDetails: false);

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'Microsoft.AspNetCore.Mvc.ProblemDetails.Extensions'");
    }

    [Fact]
    public void When_the_context_ignores_read_only_properties_then_a_getter_only_open_member_is_not_reported()
    {
        var result = Compile("public object? Value { get; }", string.Empty, contextAttributes: "[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]");

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_the_context_ignores_read_only_properties_but_the_member_is_never_ignored_then_generator_reports_BP012()
    {
        var result = Compile(
            "[JsonIgnore(Condition = JsonIgnoreCondition.Never)] public object? Value { get; }",
            string.Empty,
            contextAttributes: "[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]");

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP012");
    }

    [Fact]
    public void When_the_context_includes_fields_then_an_open_public_field_is_reported()
    {
        var result = Compile("public object? Value;", string.Empty, contextAttributes: "[JsonSourceGenerationOptions(IncludeFields = true)]");

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP012");
    }

    [Fact]
    public void When_a_field_declares_an_unregistered_runtime_type_then_generator_reports_BP013()
    {
        var result = Compile(
            "[JsonInclude, BlueprintRuntimeTypes(typeof(Unknown))] public object? Value;",
            "public sealed class Unknown { }");

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP013");
    }

    [Fact]
    public void When_the_serializer_context_is_nested_then_generator_still_reports_BP012()
    {
        var result = Compile("public object? Value { get; set; }", string.Empty, nestedContext: true);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP012");
    }

    [Fact]
    public void When_a_problem_root_lists_a_derived_problem_then_its_inherited_extensions_are_still_exempt()
    {
        // A [JsonDerivedType] alternative of the root is the root written as another type, not a value nested in another
        // response, so the runtime still writes it with fallback metadata.
        var stub = ProblemDetailsStub.Replace(
            "public class ProblemDetails",
            "[JsonDerivedType(typeof(TestNamespace.TemplateProblem), \"template\")] public class ProblemDetails",
            StringComparison.Ordinal);

        var result = Compile(
            "public int Count { get; set; }",
            "public sealed class TemplateProblem : ProblemDetails { public int Line { get; set; } }",
            problemDetailsStubOverride: stub);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_an_interface_member_declares_runtime_types_then_generator_still_reports_BP012()
    {
        // The value is written as the declared interface whatever its runtime type, so naming Circle does not make the
        // contract write Circle's members.
        var result = Compile(
            "[BlueprintRuntimeTypes(typeof(Circle))] public IShape? Value { get; set; }",
            "public interface IShape { } public sealed class Circle : IShape { public int Radius { get; set; } }",
            extraRegistrations: ["Circle"]);

        result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Equal("BP012");
    }

    [Fact]
    public void When_a_renamed_derived_member_shadows_a_renamed_open_base_member_then_generator_reports_the_base_member()
    {
        // Their JSON names differ, so System.Text.Json writes both.
        var result = Compile(
            "[JsonPropertyName(\"derivedValue\")] public new string? Value { get; set; }",
            "public class PayloadBase { [JsonPropertyName(\"baseValue\")] public object? Value { get; set; } }",
            payloadBase: "PayloadBase");

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'TestNamespace.PayloadBase.Value'");
    }

    [Fact]
    public void When_the_naming_policy_makes_a_renamed_derived_member_collide_with_the_base_member_then_generator_reports_nothing()
    {
        // Under camelCase the base Value is "value" too, so System.Text.Json writes only the derived property.
        var result = Compile(
            "[JsonPropertyName(\"value\")] public new string? Value { get; set; }",
            "public class PayloadBase { public object? Value { get; set; } }",
            payloadBase: "PayloadBase",
            contextAttributes: "[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]");

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_a_declared_root_lives_in_a_referenced_assembly_the_app_does_not_scan_then_generator_still_walks_it()
    {
        // Signature365's AdminPortal declares roots from other services' contract assemblies it does not scan; the UAT
        // 500 was a Dictionary<string, object?> on one of them.
        var contract = TestHelpers.CreateReferenceFromSource(
            "namespace Contract { public sealed class ContractRoot { public System.Collections.Generic.Dictionary<string, object?> Data { get; set; } = new(); } }",
            "Contract.Unscanned");

        var result = Compile(
            "public int Count { get; set; }",
            string.Empty,
            extraRegistrations: ["Contract.ContractRoot"],
            responseType: "Contract.ContractRoot",
            references: [contract]);

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'Contract.ContractRoot.Data'");
    }

    [Fact]
    public void When_a_nested_type_lives_in_a_referenced_assembly_that_uses_Blueprint_then_generator_walks_it()
    {
        var contract = TestHelpers.CreateReferenceFromSource(
            """
            namespace Contract
            {
                public sealed class Nested
                {
                    [Blueprint.Model.BlueprintRuntimeTypes(typeof(string))]
                    public object? Tagged { get; set; }

                    public object? Loose { get; set; }
                }
            }
            """,
            "Contract.UsesBlueprint");

        var result = Compile("public Contract.Nested? Value { get; set; }", string.Empty, extraRegistrations: ["string"], references: [contract]);

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("'Contract.Nested.Loose'");
    }

    [Theory]
    [InlineData(null, DiagnosticSeverity.Error)]
    [InlineData("warning", DiagnosticSeverity.Warning)]
    [InlineData("suggestion", DiagnosticSeverity.Info)]
    [InlineData("error", DiagnosticSeverity.Error)]
    public void When_the_consumer_configures_BP012s_severity_then_generator_reports_it_at_that_severity(string? severity, DiagnosticSeverity expected)
    {
        // The compiler applies only global configuration to a generator's diagnostics, and keeps dotnet_diagnostic keys
        // to itself, so the generator reads Blueprint's own key from an .editorconfig's [*.cs] section, where consumers
        // phase a new rule in.
        var options = severity is null ? null : new TestOptionsProvider(("blueprint_diagnostic.BP012.severity", severity));

        var result = Compile("public object? Value { get; set; }", string.Empty, options: options ?? new TestOptionsProvider());

        result.Diagnostics.Where(static diagnostic => diagnostic.Id == "BP012")
            .Should().ContainSingle()
            .Which.Severity.Should().Be(expected);
    }

    [Fact]
    public void When_the_consumer_turns_BP013_off_then_generator_does_not_report_it()
    {
        var result = Compile(
            "[BlueprintRuntimeTypes(typeof(Unknown))] public object? Value { get; set; }",
            "public sealed class Unknown { }",
            options: new TestOptionsProvider(("blueprint_diagnostic.BP013.severity", "none")));

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_a_problem_carries_open_extensions_then_generator_does_not_report_BP012()
    {
        // ProblemDetails is a declared root of every HTTP app; its Extensions are written through reflection at runtime.
        var result = Compile("public int Count { get; set; }", string.Empty);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task When_a_declared_response_root_reaches_an_open_typed_member_then_the_diagnostic_is_reported_at_the_member()
    {
        var result = Compile("public object? Value { get; set; }", string.Empty);

        await result.VerifyCompilationFailedAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    private const string ObjectAsStringConverter = """
        public sealed class ObjectAsStringConverter : JsonConverter<object>
        {
            public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();

            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
        }
        """;

    private sealed class TestOptionsProvider(params (string Key, string Value)[] values) : AnalyzerConfigOptionsProvider
    {
        private readonly TestOptions _options = new(values.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value));

        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestOptions(ImmutableDictionary<string, string>.Empty);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return this._options;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return this._options;
        }
    }

    private sealed class TestOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        // The generator's own NotNullWhenAttribute polyfill makes the BCL's ambiguous here, so the override goes without.
#pragma warning disable CS8765
        public override bool TryGetValue(string key, out string? value)
#pragma warning restore CS8765
        {
            return values.TryGetValue(key, out value);
        }
    }

    private const string ProblemDetailsStub = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class ProblemDetails
            {
                [JsonExtensionData]
                public IDictionary<string, object?> Extensions { get; set; } = new Dictionary<string, object?>();
            }

            public sealed class ValidationProblemDetails : ProblemDetails
            {
                public IDictionary<string, string[]> Errors { get; set; } = new Dictionary<string, string[]>();
            }
        }
        """;

    private const string ValidationProblemDetailsStub = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public sealed class ValidationProblemDetails : ProblemDetails
            {
            }
        }
        """;

    private const string ConvertedShapeConverter = """
        public sealed class ConvertedShapeConverter : JsonConverter<ConvertedShape>
        {
            public override ConvertedShape? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => null;

            public override void Write(Utf8JsonWriter writer, ConvertedShape value, JsonSerializerOptions options) => writer.WriteNullValue();
        }
        """;

    private static GenerationResult Compile(
        string member,
        string declarations,
        string appArguments = "",
        string[]? extraRegistrations = null,
        string? payloadBase = null,
        string contextAttributes = "",
        bool nestedContext = false,
        bool stubProblemDetails = true,
        string? problemDetailsStubOverride = null,
        string responseType = "Payload",
        MetadataReference[]? references = null,
        AnalyzerConfigOptionsProvider? options = null)
    {
        var problemDetailsStub = problemDetailsStubOverride ?? (stubProblemDetails ? ProblemDetailsStub : ValidationProblemDetailsStub);
        var contextOpen = nestedContext ? "public static partial class Outer {" : string.Empty;
        var contextClose = nestedContext ? "}" : string.Empty;
        var contextReference = nestedContext ? "Outer.TestBlueprintJsonContext" : "TestBlueprintJsonContext";
        var registrations = string.Concat((extraRegistrations ?? []).Select(static type => $"\n        [JsonSerializable(typeof({type}))]"));
        var baseClause = payloadBase is null ? string.Empty : " : " + payloadBase;

        var source = $$"""
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using Blueprint.Model;
            using Blueprint.Model.Attributes;
            using Blueprint.Runtime.Exec;
            using Microsoft.AspNetCore.Mvc;

            {{problemDetailsStub}}

            namespace Blueprint.Model.Attributes
            {
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
                internal sealed class RootLinkAttribute : Attribute
                {
                    public RootLinkAttribute(string url)
                    {
                    }
                }
            }

            namespace TestNamespace
            {
                public sealed class Payload{{baseClause}}
                {
                    {{member}}
                }

                {{declarations}}

                {{contextOpen}}
                {{contextAttributes}}
                [JsonSerializable(typeof(ProblemDetails))]
                [JsonSerializable(typeof(ValidationProblemDetails))]
                [JsonSerializable(typeof(Payload))]{{registrations}}
                public partial class TestBlueprintJsonContext : JsonSerializerContext
                {
                    public TestBlueprintJsonContext()
                        : base(new JsonSerializerOptions())
                    {
                    }

                    protected override JsonSerializerOptions? GeneratedSerializerOptions => new JsonSerializerOptions();

                    public override JsonTypeInfo? GetTypeInfo(Type type)
                    {
                        return null;
                    }
                }
                {{contextClose}}

                [RootLink("/open-typed-members")]
                public partial class IncludedOperation : IQuery<OkResult<{{responseType}}>>
                {
                    public static ApiOperationDescriptor Configure() => throw new NotImplementedException();
                }

                [BlueprintAppScan]
                [BlueprintAppSerializerContext(JsonSerializerContextType = typeof({{contextReference}}){{appArguments}})]
                public partial class TestBlueprintApp : BlueprintApp
                {
                }
            }
            """;

        return references is null && options is null
            ? TestHelpers.Compile<AppDataModelSourceGenerator>(source)
            : TestHelpers.CompileOnce<AppDataModelSourceGenerator>(source, options, references);
    }
}
