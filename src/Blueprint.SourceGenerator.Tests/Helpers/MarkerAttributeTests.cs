using Blueprint.SourceGenerator.Helpers.Attributes;

namespace Blueprint.SourceGenerator.Tests.Generators.Helpers;

public class MarkerAttributeTests
{
    [Fact]
    public async Task When_no_constructor_arguments_then_generates_correct_source()
    {
        var attribute = new MarkerAttribute("Blueprint.Tests.GeneratedAttribute")
        {
            Documentation = "This is a generated attribute.",
        };

        var generatedSource = attribute.GenerateSource();
        await Verify(generatedSource);
    }

    [Fact]
    public async Task When_AttributeUsage_specified_then_generates_correct_source()
    {
        var attribute = new MarkerAttribute("Blueprint.Tests.GeneratedAttribute", new AttributeUsageAttribute(AttributeTargets.Property) { AllowMultiple = true, Inherited = false })
        {
            Documentation = "This is a generated attribute.",
        };

        var generatedSource = attribute.GenerateSource();
        await Verify(generatedSource);
    }

    [Fact]
    public async Task When_single_constructor_argument_then_generates_correct_source()
    {
        var constructorArgument = new MarkerAttributeConstructorArgument<string, string>("url", "The route for this link, may contain placeholders.");

        var attribute = new MarkerAttribute("Blueprint.Tests.GeneratedAttribute")
        {
            Documentation = "This is a generated attribute.",

            ConstructorArguments =
            {
                constructorArgument,
            },
        };

        var generatedSource = attribute.GenerateSource();
        await Verify(generatedSource);
    }
}
