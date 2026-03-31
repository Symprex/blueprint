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
}
