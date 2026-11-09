using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

/// <summary>
/// <c>[ProducesContentType]</c> is read from the attribute's syntax, never by running its constructor, so the generator
/// itself rejects a declaration that is not a media type: it would otherwise become an invalid OpenAPI content key.
/// </summary>
public class OperationDescriptorSourceGeneratorTestsProducesContentTypeTests
{
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("\"text/\"")]
    [InlineData("\"/vcard\"")]
    [InlineData("\"text/v card\"")]
    [InlineData("\"text/vcard/extra\"")]
    [InlineData("\" text/vcard\"")]
    [InlineData("\"text/vcard\", \"\"")]
    [InlineData("\"text/vcard\", null")]
    [InlineData("\"text/vcard\", (string)null")]
    public void When_a_declared_content_type_is_not_a_media_type_then_BPOA003_is_reported_at_the_attribute(string arguments)
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile($$"""
            [RootLink("/produces-content-type/invalid")]
            [ProducesContentType({{arguments}})]
            public partial class InvalidContentTypeQuery : IQuery<Blueprint.Http.FileStreamResult> {}
            """);

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BPOA003").Subject;

        diagnostic.GetMessage().Should().Contain("InvalidContentTypeQuery");
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)
            .Should().StartWith("ProducesContentType(");
    }

    [Theory]
    [InlineData("\"text/vcard\"")]
    [InlineData("\"text/vcard; charset=utf-8\"")]
    [InlineData("\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet\"")]
    [InlineData("\"application/pdf\", \"image/png\", \"application/problem+json\"")]
    public void When_every_declared_content_type_is_a_media_type_then_BPOA003_is_not_reported(string arguments)
    {
        var result = OperationDescriptorSourceGeneratorTests.Compile($$"""
            [RootLink("/produces-content-type/valid")]
            [ProducesContentType({{arguments}})]
            public partial class ValidContentTypeQuery : IQuery<Blueprint.Http.FileStreamResult> {}
            """);

        result.Diagnostics.Select(static d => d.Id).Should().NotContain("BPOA003");
    }
}
