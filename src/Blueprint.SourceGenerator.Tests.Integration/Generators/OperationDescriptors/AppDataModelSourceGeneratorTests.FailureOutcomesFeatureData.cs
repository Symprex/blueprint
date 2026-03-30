using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorFailureOutcomesFeatureDataTests
{
    [Fact]
    public void When_descriptor_is_configured_then_failure_outcomes_feature_data_matches_default_translator_order()
    {
        var data = FailureMetadataQueryOp.Configure().GetFeatureData<OperationFailureOutcomesFeatureData>();

        data.Should().NotBeNull();
        data!.Outcomes.Should().Equal(
            new OperationFailureOutcome(
                "legacy-validation-exception",
                "Blueprint.Validation.ValidationException",
                "Blueprint.Runtime.Exec.ValidationFailedResult"),
            new OperationFailureOutcome(
                "data-annotations-validation-exception",
                "System.ComponentModel.DataAnnotations.ValidationException",
                "Blueprint.Runtime.Exec.ValidationFailedResult"),
            new OperationFailureOutcome(
                "default-unhandled-exception",
                "System.Exception",
                "Blueprint.Runtime.Exec.UnhandledExceptionResult"));
    }
}

public partial class FailureMetadataQueryOp : IQuery<string>
{
    public string Handle()
    {
        return "OK";
    }
}
