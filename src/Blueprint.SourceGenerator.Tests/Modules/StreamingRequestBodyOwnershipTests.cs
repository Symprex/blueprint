using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Tests <see cref="Blueprint.SourceGenerator.Modules.PropertyOwnershipModule"/>'s two request bodies besides JSON (#71, D19):
/// a body-owned <c>Stream</c> handed the request body unread, and a <c>multipart/form-data</c> body for an operation with an
/// <c>IFormFile</c> property, with the diagnostics that keep each coherent (BPPO014-016).
/// </summary>
public class StreamingRequestBodyOwnershipTests
{
    // The test compilation references the assemblies this process has loaded; IFormFile's must be one of them.
    private static readonly Type _formFileType = typeof(Microsoft.AspNetCore.Http.IFormFile);

    [Fact]
    public async Task When_a_FromBody_Stream_is_declared_then_the_pipeline_hands_it_the_request_body()
    {
        await VerifyPipelineAsync(
            """
            [RootLink("/imports/{FolderId}")]
            [HttpPost]
            public partial class ImportCommand : ICommand<string>
            {
                public string FolderId { get; set; }

                [FromQuery]
                public int? Limit { get; set; }

                [FromBody]
                public System.IO.Stream Content { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);
    }

    [Fact]
    public async Task When_an_IFormFile_is_declared_then_the_pipeline_reads_files_and_fields_from_the_form()
    {
        await VerifyPipelineAsync(
            """
            [RootLink("/folders/{FolderId}/attachments")]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                public string FolderId { get; set; }
                public string Description { get; set; }
                public int Priority { get; set; }
                public System.Guid[] Related { get; set; }
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);
    }

    [Fact]
    public void When_a_Stream_is_body_owned_by_convention_then_it_takes_the_body_without_a_diagnostic()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/imports")]
            [HttpPut]
            public partial class ImportCommand : ICommand<string>
            {
                public System.IO.Stream Content { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_a_Stream_body_has_another_body_property_then_reports_BPPO014()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/imports")]
            [HttpPost]
            public partial class ImportCommand : ICommand<string>
            {
                [FromBody]
                public System.IO.Stream Content { get; set; }

                public string Label { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("BPPO014");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("'Content'").And.Contain("'Label'");
    }

    [Fact]
    public void When_a_Stream_body_has_a_file_property_then_reports_BPPO014()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/imports")]
            [HttpPost]
            public partial class ImportCommand : ICommand<string>
            {
                [FromBody]
                public System.IO.Stream Content { get; set; }

                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Select(static d => d.Id).Should().Equal("BPPO014");
    }

    [Fact]
    public void When_a_Stream_body_has_a_route_placeholder_in_only_some_links_then_reports_BPPO014()
    {
        // A placeholder missing from one link falls back to the body through that link, which a stream leaves nothing of.
        var result = CompileDescriptor(
            """
            [RootLink("/folders/{FolderId}/imports")]
            [RootLink("/imports")]
            [HttpPost]
            public partial class ImportCommand : ICommand<string>
            {
                public string FolderId { get; set; }

                [FromBody]
                public System.IO.Stream Content { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Select(static d => d.Id).Should().Equal("BPPO014");
    }

    [Fact]
    public void When_a_Stream_or_file_is_on_an_operation_that_also_answers_GET_then_reports_BPPO015()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/attachments")]
            [HttpGet]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);

        // BPOA002 warns about the two methods themselves; BPPO015 is the error that stops the build.
        var diagnostic = result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("BPPO015");
        diagnostic.GetMessage().Should().Contain("'File'");
    }

    [Fact]
    public void When_a_multipart_operation_has_a_property_no_form_field_can_carry_then_reports_BPPO016()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/attachments")]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public System.Collections.Generic.Dictionary<string, string> Metadata { get; set; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("BPPO016");
        diagnostic.GetMessage().Should().Contain("'Metadata'");
    }

    [Fact]
    public void When_a_multipart_operation_has_scalar_enum_and_array_fields_then_reports_nothing()
    {
        var result = CompileDescriptor(
            """
            public enum Kind { A, B }

            [RootLink("/attachments")]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }
                public Microsoft.AspNetCore.Http.IFormFile Thumbnail { get; set; }
                public string Name { get; set; }
                public Kind Kind { get; set; }
                public System.DateTimeOffset? Taken { get; set; }
                public string[] Tags { get; set; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_a_multipart_field_is_an_array_of_a_nullable_value_type_then_reports_BPPO016()
    {
        // A form field array is parsed into an array of the element's underlying type, which cannot be assigned to int?[].
        var result = CompileDescriptor(
            """
            [RootLink("/attachments")]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public int?[] Scores { get; set; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("BPPO016");
        diagnostic.GetMessage().Should().Contain("'Scores'");
    }

    [Fact]
    public void When_a_multipart_field_is_a_multidimensional_array_then_reports_BPPO016()
    {
        // A form field array is parsed into a one-dimensional array, which cannot be assigned to int[,].
        var result = CompileDescriptor(
            """
            [RootLink("/attachments")]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public int[,] Grid { get; set; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("BPPO016");
        diagnostic.GetMessage().Should().Contain("'Grid'");
    }

    [Fact]
    public void When_a_body_Stream_has_no_getter_then_reports_BPPO017()
    {
        // A client sends the stream it reads from the property; one it cannot read used to generate a builder that did not compile.
        var result = CompileDescriptor(
            """
            [RootLink("/imports")]
            [HttpPost]
            public partial class ImportCommand : ICommand<string>
            {
                private System.IO.Stream _content;

                [FromBody]
                public System.IO.Stream Content { set => _content = value; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("BPPO017");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Contain("'Content'");
    }

    [Fact]
    public void When_a_multipart_field_has_no_getter_then_reports_BPPO017()
    {
        var result = CompileDescriptor(
            """
            [RootLink("/attachments")]
            [HttpPost]
            public partial class AttachCommand : ICommand<string>
            {
                private string _label;

                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public string Label { set => _label = value; }

                public static Task<string> HandleAsync(AttachCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Select(static d => d.Id).Should().Equal("BPPO017");
    }

    [Fact]
    public void When_a_Stream_hides_a_base_property_of_the_same_name_then_the_client_body_is_built_from_the_Stream()
    {
        // The derived property is the one bound; the client's body builder used to look the name up and find the base's.
        var result = CompileDescriptor(
            """
            public abstract partial class ImportBase
            {
                public string Content { get; set; }
            }

            [RootLink("/imports")]
            [HttpPost]
            public partial class ImportCommand : ImportBase, ICommand<string>
            {
                [FromBody]
                public new System.IO.Stream Content { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Output.Select(static t => t.ToString()).Should().Contain(static s => s.Contains("CreateStreamContent(this.Content)"));
    }

    [Fact]
    public void When_the_body_Stream_is_obsolete_then_the_client_body_is_still_built_from_it()
    {
        // The host still binds an obsolete property; the client's body builder used to look it up among the non-obsolete ones.
        var result = CompileDescriptor(
            """
            [RootLink("/imports")]
            [HttpPost]
            public partial class ImportCommand : ICommand<string>
            {
                [System.Obsolete("Use the new import.")]
                [FromBody]
                public System.IO.Stream Content { get; set; }

                public static Task<string> HandleAsync(ImportCommand operation) => Task.FromResult("ok");
            }
            """);

        result.Diagnostics.Should().BeEmpty();
        result.Output.Select(static t => t.ToString()).Should().Contain(static s => s.Contains("CreateStreamContent(this.Content)"));
    }

    [Fact]
    public async Task When_a_route_placeholder_falls_back_to_a_form_beside_a_property_named_for_its_fallback_then_the_pipeline_compiles()
    {
        // {Name} is in only one link, so it also falls back to the form, as the variables of a property named NameFallback.
        await VerifyPipelineAsync(
            """
            [RootLink("/items/{Name}")]
            [RootLink("/items")]
            [HttpPost]
            public partial class AttachItemCommand : ICommand<string>
            {
                public string Name { get; set; }
                public string NameFallback { get; set; }
                public Microsoft.AspNetCore.Http.IFormFile File { get; set; }

                public static Task<string> HandleAsync(AttachItemCommand operation) => Task.FromResult("ok");
            }
            """);
    }

    private static GenerationResult CompileDescriptor(string source)
    {
        _ = _formFileType;

        return TestHelpers.Compile<OperationDescriptorSourceGenerator>(
            "using Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
            + "namespace TestNamespace;\n\n" + source);
    }

    private static async Task VerifyPipelineAsync(string operationSource)
    {
        _ = _formFileType;

        var source = "using Blueprint;\nusing Blueprint.Model;\nusing Blueprint.Model.Attributes;\nusing System.Threading.Tasks;\n\n"
            + "namespace TestNamespace;\n\n[BlueprintAppScan]\npublic partial class TestApp : BlueprintApp { }\n\n" + operationSource;

        var result = TestHelpers.CompileWithTrackedGenerators(
            [
                TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
                TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
            ],
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var outputText = new StringBuilder();
        foreach (var generated in result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator)))
        {
            outputText.AppendLine(generated.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(generated.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Generators/Pipelines/Snapshots/Integration/");
    }
}
