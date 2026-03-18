using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Planning;

public class DescriptorPlanTests
{
    [Fact]
    public void When_single_metadata_added_then_try_get_returns_it()
    {
        var plan = new DescriptorPlan(CreateOperationModel());
        var metadata = new TestMetadata("only");

        plan.AddMetadata(metadata);

        plan.TryGetMetadata<TestMetadata>(out var result).Should().BeTrue();
        result.Should().Be(metadata);
    }

    [Fact]
    public void When_no_metadata_added_then_try_get_returns_false()
    {
        var plan = new DescriptorPlan(CreateOperationModel());

        plan.TryGetMetadata<TestMetadata>(out var metadata).Should().BeFalse();
        metadata.Should().BeNull();
    }

    [Fact]
    public void When_metadata_added_then_single_and_multiple_queries_work()
    {
        var plan = new DescriptorPlan(CreateOperationModel());
        var first = new TestMetadata("first");
        var second = new TestMetadata("second");

        plan.AddMetadata(first);
        plan.AddMetadata(second);

        plan.TryGetMetadata<TestMetadata>(out var metadata).Should().BeFalse();
        plan.GetMetadata<TestMetadata>().Should().Equal(first, second);
    }

    [Fact]
    public void When_render_steps_added_then_they_render_in_order()
    {
        var plan = new DescriptorPlan(CreateOperationModel());

        plan.AddRenderStep(writer => writer.AppendLine("first();"));
        plan.AddRenderStep(writer => writer.AppendLine("second();"));

        var builder = CodeBuilder.Create("TestNamespace");
        var codeWriter = builder.AddClass("Container")
            .AddMethod("Render")
            .WithBody(plan.Render);

        var generated = builder.Build();

        generated.Should().Contain("first();");
        generated.Should().Contain("second();");
        generated.IndexOf("first();", StringComparison.Ordinal).Should().BeLessThan(generated.IndexOf("second();", StringComparison.Ordinal));
        codeWriter.Should().NotBeNull();
    }

    private static OperationModel CreateOperationModel()
    {
        const string source = """
                              using Blueprint.Model;

                              namespace TestNamespace;

                              public partial class SampleOperation : IOperation
                              {
                              }
                              """;

        var compilation = CreateCompilation(source);
        var symbol = compilation.GetTypeByMetadataName("TestNamespace.SampleOperation");

        return OperationModel.Create(symbol!);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "DescriptorPlanTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed record TestMetadata(string Name);
}
