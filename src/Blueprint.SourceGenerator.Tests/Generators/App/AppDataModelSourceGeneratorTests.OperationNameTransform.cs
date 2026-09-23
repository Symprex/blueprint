using System.Globalization;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Tests.Generators.AppDataModel;

/// <summary>
/// Proves how <c>[BlueprintAppOperationNames(Remove = ...)]</c> reaches the generated app registration, and that
/// collisions are detected on the names the app actually registers: the removal runs after <c>Query</c>/<c>Command</c>
/// suffix stripping and removes every occurrence, as legacy Blueprint's <c>IOperationScannerConvention</c> did.
/// </summary>
public class AppDataModelSourceGeneratorTests_OperationNameTransform
{
    [Fact]
    public void When_removal_gives_two_operations_the_same_name_then_reports_BP011()
    {
        // "FooSampleQuery" strips to "FooSample", and only removing every occurrence (not just a prefix) makes it "Foo".
        var result = Compile(
            Operation("FooSampleQuery") + Operation("FooCommand") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BP011").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Be(
            "Operations FooSampleQuery, FooCommand produce the same descriptor name 'Foo' after the app removes 'Sample' "
            + "from operation names. Rename one of them, or change the app's [BlueprintAppOperationNames], to avoid an "
            + "OpenAPI operationId collision.");
        result.Diagnostics.Should().NotContain(static d => d.Id == "BP009", "the suffix-stripped names were distinct");
    }

    [Fact]
    public void When_removal_exposes_a_suffix_then_names_stay_distinct_because_stripping_ran_first()
    {
        // Strip then remove: "ExportCommandSample" -> "ExportCommand" and "ExportQuery" -> "Export". Removing first would
        // name both "Export".
        var result = Compile(
            Operation("ExportCommandSample") + Operation("ExportQuery") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        result.Diagnostics.Should().NotContain(static d => d.Id == "BP011" || d.Id == "BP009");
    }

    [Fact]
    public void When_suffix_stripped_names_already_collide_then_reports_BP009_only()
    {
        var result = Compile(
            Operation("FooQuery") + Operation("FooCommand") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        result.Diagnostics.Where(static d => d.Id == "BP009").Should().ContainSingle();
        result.Diagnostics.Should().NotContain(static d => d.Id == "BP011", "the removal did not cause this collision");
    }

    [Fact]
    public void When_removal_leaves_an_operation_with_an_empty_name_then_reports_BP011()
    {
        // "SampleQuery" strips to "Sample", and removing "Sample" leaves nothing to use as its operationId.
        var result = Compile(
            Operation("SampleQuery") + Operation("FooCommand") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        var diagnostic = result.Diagnostics.Should().ContainSingle(static d => d.Id == "BP011").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage(CultureInfo.InvariantCulture).Should().Be(
            "The app's removal of 'Sample' from operation names leaves SampleQuery with an empty descriptor name. "
            + "Rename it, or change the app's [BlueprintAppOperationNames], so its OpenAPI operationId is not empty.");
    }

    [Fact]
    public void When_removal_empties_several_operation_names_then_reports_them_as_empty_rather_than_colliding()
    {
        // "SampleQuery" -> "Sample" and "SampleSampleCommand" -> "SampleSample" both become "": an empty name is the
        // problem to report, not that the two empty names are equal.
        var result = Compile(
            Operation("SampleQuery") + Operation("SampleSampleCommand") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        result.Diagnostics.Where(static d => d.Id == "BP011")
            .Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
            .Should()
            .Equal(
                "The app's removal of 'Sample' from operation names leaves SampleQuery, SampleSampleCommand with an empty "
                + "descriptor name. Rename it, or change the app's [BlueprintAppOperationNames], so its OpenAPI "
                + "operationId is not empty.");
    }

    [Fact]
    public void When_an_operation_name_matches_the_removal_only_by_case_then_it_is_not_removed()
    {
        // Ordinal, as legacy's string.Replace was: "SAMPLEFooQuery" keeps "SAMPLEFoo", so it does not collide with "Foo".
        var result = Compile(
            Operation("SAMPLEFooQuery") + Operation("FooCommand") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        result.Diagnostics.Should().NotContain(static d => d.Id == "BP011");
    }

    [Fact]
    public void When_app_declares_no_removal_then_names_it_would_have_merged_are_not_reported()
    {
        var result = Compile(Operation("SampleFooQuery") + Operation("FooCommand") + App(string.Empty));

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void When_app_removes_substring_then_registration_renames_each_descriptor_before_registering_it()
    {
        var result = Compile(Operation("SampleFooQuery") + App("""[BlueprintAppOperationNames(Remove = "Sample")]"""));

        result.Diagnostics.Should().BeEmpty("the emitted rename must compile");

        var registration = RegistrationSource(result);
        registration.Should().Contain("this.Register(ApplyOperationNameTransform(TestNamespace.SampleFooQuery.Configure()));");
        registration.Should().Contain(
            "descriptor.Name = descriptor.Name.Replace(\"Sample\", string.Empty, global::System.StringComparison.Ordinal);");
        registration.IndexOf("this.Register(", StringComparison.Ordinal).Should().BeLessThan(
            registration.IndexOf("this.Freeze();", StringComparison.Ordinal));
    }

    [Fact]
    public void When_removal_is_empty_then_registration_is_unchanged()
    {
        // string.Replace throws on an empty oldValue, so an empty removal must never reach the app constructor.
        var result = Compile(Operation("SampleFooQuery") + App("""[BlueprintAppOperationNames(Remove = "")]"""));

        result.Diagnostics.Should().BeEmpty();

        var registration = RegistrationSource(result);
        registration.Should().Contain("this.Register(TestNamespace.SampleFooQuery.Configure());");
        registration.Should().NotContain("ApplyOperationNameTransform");
    }

    private static string Operation(string typeName)
    {
        return $$"""
                 public partial class {{typeName}} : IOperation
                 {
                     public static ApiOperationDescriptor Configure() => throw new System.NotImplementedException();
                 }

                 """;
    }

    private static string App(string attribute)
    {
        return $$"""
                 [BlueprintAppScan]
                 {{attribute}}
                 public partial class TestBlueprintApp : BlueprintApp
                 {
                 }
                 """;
    }

    private static string RegistrationSource(GenerationResult result)
    {
        return result.Output
            .Where(static tree => tree.FilePath.EndsWith("TestBlueprintApp_DataModelRegistration.g.cs", StringComparison.Ordinal))
            .Select(static tree => tree.ToString())
            .Single();
    }

    private static GenerationResult Compile(string source)
    {
        return AppDataModelSourceGeneratorTests.Compile(source);
    }
}
