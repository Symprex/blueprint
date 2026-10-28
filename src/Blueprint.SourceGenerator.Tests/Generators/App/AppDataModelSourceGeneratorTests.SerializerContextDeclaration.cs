using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

public class AppDataModelSourceGeneratorTestsSerializerContextDeclaration
{
    [Fact]
    public async Task When_app_declares_serializer_context_then_generated_registration_assigns_declared_serializer_context_type()
    {
        var source = """
                     #nullable enable
                     
                     using System;
                     using System.Text.Json;
                     using System.Text.Json.Serialization;
                     using System.Text.Json.Serialization.Metadata;
                     
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
                     
                     public partial class IncludedOperation : IOperation
                     {
                         public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                     }
                     
                     [BlueprintAppScan]
                     [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext))]
                     public partial class TestBlueprintApp : BlueprintApp
                     {
                     }
                     """;

        await AppDataModelSourceGeneratorTests.Compile(source)
            .VerifySuccessAsync(AppDataModelSourceGeneratorTests.SnapshotsFolder);
    }

    [Fact]
    public void When_app_declares_serializer_context_then_generated_registration_assigns_declared_serializer_context_body_type_names()
    {
        var result = AppDataModelSourceGeneratorTests.Compile("""
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            public sealed class FirstPayload
            {
            }

            public sealed class SecondPayload
            {
            }

            [JsonSerializable(typeof(string))]
            [JsonSerializable(typeof(FirstPayload))]
            [JsonSerializable(typeof(SecondPayload))]
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

            public partial class IncludedOperation : IOperation
            {
                public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
            }

            [BlueprintAppScan]
            [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext))]
            public partial class TestBlueprintApp : BlueprintApp
            {
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var registrationSource = result.Output
            .Where(static tree => tree.FilePath.EndsWith("TestBlueprintApp_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .SingleOrDefault();

        registrationSource.Should().NotBeNull();
        registrationSource!.Should().Contain("this.AppOwnedSerializerContextBodyTypeNames");
        registrationSource.Should().Contain("\"string\"");
        registrationSource.Should().Contain("\"TestNamespace.FirstPayload\"");
        registrationSource.Should().Contain("\"TestNamespace.SecondPayload\"");
    }

    [Fact]
    public void When_app_declares_serializer_context_then_generated_registration_assigns_declared_serializer_context_instance()
    {
        var result = AppDataModelSourceGeneratorTests.Compile("""
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            [JsonSerializable(typeof(string))]
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

            public partial class IncludedOperation : IOperation
            {
                public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
            }

            [BlueprintAppScan]
            [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext))]
            public partial class TestBlueprintApp : BlueprintApp
            {
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var registrationSource = result.Output
            .Where(static tree => tree.FilePath.EndsWith("TestBlueprintApp_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .SingleOrDefault();

        registrationSource.Should().NotBeNull();
        registrationSource!.Should().Contain("this.AppOwnedSerializerContext = new global::TestNamespace.TestBlueprintJsonContext();");
    }

    [Fact]
    public void When_app_declares_serializer_context_then_generated_registration_assigns_declared_serializer_context_body_types()
    {
        var result = AppDataModelSourceGeneratorTests.Compile("""
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

            public sealed class FirstPayload
            {
            }

            [JsonSerializable(typeof(string))]
            [JsonSerializable(typeof(FirstPayload))]
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

            public partial class IncludedOperation : IOperation
            {
                public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
            }

            [BlueprintAppScan]
            [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext))]
            public partial class TestBlueprintApp : BlueprintApp
            {
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var registrationSource = result.Output
            .Where(static tree => tree.FilePath.EndsWith("TestBlueprintApp_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .SingleOrDefault();

        registrationSource.Should().NotBeNull();
        registrationSource!.Should().Contain("this.AppOwnedSerializerContextBodyTypes = global::System.Collections.Immutable.ImmutableArray.Create<global::System.Type>(typeof(string), typeof(global::TestNamespace.FirstPayload));");
    }

    [Theory]
    [InlineData("AllowRuntimeTypeFallback = true", true)]
    [InlineData("AllowRuntimeTypeFallback = false", false)]
    [InlineData(null, false)]
    public void When_app_declares_runtime_type_fallback_then_generated_registration_assigns_it_only_when_true(string? argument, bool assigned)
    {
        var arguments = argument is null ? string.Empty : ", " + argument;
        var result = AppDataModelSourceGeneratorTests.Compile($$"""
            #nullable enable

            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;

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

            public partial class IncludedOperation : IOperation
            {
                public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
            }

            [BlueprintAppScan]
            [BlueprintAppSerializerContext(JsonSerializerContextType = typeof(TestBlueprintJsonContext){{arguments}})]
            public partial class TestBlueprintApp : BlueprintApp
            {
            }
            """);

        result.Diagnostics.Should().BeEmpty();

        var registrationSource = result.Output
            .Where(static tree => tree.FilePath.EndsWith("TestBlueprintApp_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .Single();

        if (assigned)
        {
            registrationSource.Should().Contain("this.AllowRuntimeTypeFallback = true;");
        }
        else
        {
            registrationSource.Should().NotContain("AllowRuntimeTypeFallback");
        }
    }
}
