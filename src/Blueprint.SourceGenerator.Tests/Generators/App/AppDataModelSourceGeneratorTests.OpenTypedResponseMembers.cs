using Blueprint.SourceGenerator.Generators.App;
using FluentAssertions;

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
        string? payloadBase = null)
    {
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

                [RootLink("/open-typed-members")]
                public partial class IncludedOperation : IQuery<OkResult<Payload>>
                {
                    public static ApiOperationDescriptor Configure() => throw new NotImplementedException();
                }

                [BlueprintAppScan]
                [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext){{appArguments}})]
                public partial class TestBlueprintApp : BlueprintApp
                {
                }
            }
            """;

        return TestHelpers.Compile<AppDataModelSourceGenerator>(source);
    }
}
