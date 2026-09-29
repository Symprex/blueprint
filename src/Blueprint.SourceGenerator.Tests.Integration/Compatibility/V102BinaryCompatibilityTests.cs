using System.Collections.Immutable;
using System.Reflection;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Compatibility;

/// <summary>
/// Proves that the public members an assembly compiled against Blueprint 1.0.2 binds to still exist with their 1.0.2
/// signatures, so it runs against this patch release without <see cref="MissingMethodException"/>.
/// </summary>
/// <remarks>
/// <para>
/// A compiler binds a call to the exact signature it saw. Adding an optional parameter to a public constructor or method
/// keeps source compatible (a recompile fills the default in) but removes the signature an existing binary calls. The
/// signatures here are the 1.0.2 ones, read from <c>git show v1.0.2</c> and confirmed by comparing the 1.0.2 packages
/// with this build (<c>Microsoft.DotNet.ApiCompat.Tool</c>, CP0002), not taken from the current code.
/// </para>
/// <para>
/// <see cref="OperationHttpResponseMetadata"/>'s four-argument constructor is what the 1.0.2 generator emits into every
/// operation's descriptor (<c>HttpResponseMetadataModule.cs:493,506</c> at <c>v1.0.2</c>), so an operation assembly built
/// with 1.0.2 fails to describe itself without it. <c>OpenApiDocumentRenderingContext.Create(OpenApiPathItemRenderingContext)</c>
/// is the rendering chain's public entry point, and <see cref="OpenApiResponseObjectModel"/> is what an operation processor
/// (<see cref="OpenApiOperationProcessorOptions"/>) receives and may construct.
/// </para>
/// </remarks>
public sealed class V102BinaryCompatibilityTests
{
    private static readonly Type[] _operationHttpResponseMetadataV102 =
    [
        typeof(int),
        typeof(bool),
        typeof(ImmutableArray<string>),
        typeof(ImmutableArray<string>),
    ];

    private static readonly Type[] _openApiResponseObjectModelV102 =
    [
        typeof(int),
        typeof(bool),
        typeof(ImmutableArray<string>),
        typeof(ImmutableArray<OpenApiResponseContentModel>),
        typeof(ImmutableArray<OpenApiResponseHeaderModel>),
    ];

    [Fact]
    public void OperationHttpResponseMetadata_keeps_its_1_0_2_constructor()
    {
        var constructor = typeof(OperationHttpResponseMetadata).GetConstructor(_operationHttpResponseMetadataV102);

        constructor.Should().NotBeNull();

        var metadata = (OperationHttpResponseMetadata)constructor!.Invoke([404, true, ImmutableArray.Create("NotFoundResult"), ImmutableArray.Create("Widget")]);

        metadata.StatusCode.Should().Be(404);
        metadata.HasResponseBody.Should().BeTrue();
        metadata.ResultTypeNames.Should().Equal("NotFoundResult");
        metadata.BodyTypeNames.Should().Equal("Widget");
        metadata.ProblemExamples.IsDefaultOrEmpty.Should().BeTrue();
    }

    [Fact]
    public void OperationHttpResponseMetadata_keeps_its_1_0_2_Deconstruct()
    {
        var deconstruct = typeof(OperationHttpResponseMetadata).GetMethod(
            "Deconstruct",
            [.. _operationHttpResponseMetadataV102.Select(static type => type.MakeByRefType())]);

        deconstruct.Should().NotBeNull();

        var arguments = new object?[4];
        deconstruct!.Invoke(new OperationHttpResponseMetadata(201, false, ["CreatedResult"], ["Widget"]), arguments);

        arguments[0].Should().Be(201);
        arguments[1].Should().Be(false);
        ((ImmutableArray<string>)arguments[2]!).Should().Equal("CreatedResult");
        ((ImmutableArray<string>)arguments[3]!).Should().Equal("Widget");
    }

    [Fact]
    public void OpenApiDocumentRenderingContext_keeps_its_1_0_2_Create()
    {
        var create = typeof(OpenApiDocumentRenderingContext).GetMethod(
            nameof(OpenApiDocumentRenderingContext.Create),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(OpenApiPathItemRenderingContext)]);

        create.Should().NotBeNull();
        create!.ReturnType.Should().Be<OpenApiDocumentRenderingContext>();

        // As in 1.0.2, it renders the framework's own ResourceEvent envelope text, not legacy Blueprint's.
        var pathItems = CreatePathItemRenderingContext(new IntegrationTestBlueprintApp());
        var rendered = Render((OpenApiDocumentRenderingContext)create.Invoke(null, [pathItems])!);

        rendered.Should().Be(Render(OpenApiDocumentRenderingContext.Create(pathItems, useLegacyResourceEventText: false)));
        rendered.Should().NotBe(Render(OpenApiDocumentRenderingContext.Create(pathItems, useLegacyResourceEventText: true)));
    }

    [Fact]
    public void OpenApiResponseObjectModel_keeps_its_1_0_2_constructor_and_Deconstruct()
    {
        var constructor = typeof(OpenApiResponseObjectModel).GetConstructor(_openApiResponseObjectModelV102);
        var deconstruct = typeof(OpenApiResponseObjectModel).GetMethod(
            "Deconstruct",
            [.. _openApiResponseObjectModelV102.Select(static type => type.MakeByRefType())]);

        constructor.Should().NotBeNull();
        deconstruct.Should().NotBeNull();

        var model = (OpenApiResponseObjectModel)constructor!.Invoke(
            [200, true, ImmutableArray.Create("OkResult"), ImmutableArray<OpenApiResponseContentModel>.Empty, ImmutableArray<OpenApiResponseHeaderModel>.Empty]);

        model.StatusCode.Should().Be(200);
        model.ProblemExamples.IsDefaultOrEmpty.Should().BeTrue();

        var arguments = new object?[5];
        deconstruct!.Invoke(model, arguments);

        arguments[0].Should().Be(200);
        ((ImmutableArray<string>)arguments[2]!).Should().Equal("OkResult");
    }

    private static OpenApiPathItemRenderingContext CreatePathItemRenderingContext(BlueprintApp app)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);

        return OpenApiPathItemRenderingContext.Create(operationContext);
    }

    private static string Render(OpenApiDocumentRenderingContext documentContext)
    {
        return OpenApiOutputRenderingContext.Create(documentContext, infoOptions: null, schemaProcessorOptions: null).Json;
    }
}
