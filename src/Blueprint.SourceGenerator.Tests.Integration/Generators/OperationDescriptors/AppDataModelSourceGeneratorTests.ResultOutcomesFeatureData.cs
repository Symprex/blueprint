using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorResultOutcomesFeatureDataTests
{
    [Fact]
    public void When_declared_contract_is_results_union_then_Configure_sets_flattened_result_outcomes_feature_data()
    {
        var data = ResultUnionQueryOp.Configure().GetFeatureData<OperationResultOutcomesFeatureData>();
        data.Should().NotBeNull();

        data!.Outcomes.Select(static outcome => outcome.ResultType)
            .Should()
            .Equal(typeof(OkResult<string>), typeof(NoResultOperationResult));
    }

    [Fact]
    public void When_declared_contract_is_explicit_semantic_result_then_Configure_sets_single_flattened_result_outcome()
    {
        var data = ExplicitSemanticResultQueryOp.Configure().GetFeatureData<OperationResultOutcomesFeatureData>();
        data.Should().NotBeNull();

        data!.Outcomes.Select(static outcome => outcome.ResultType)
            .Should()
            .Equal(typeof(OkResult<string>));
    }
}

public partial class ResultUnionQueryOp : IQuery<Results<OkResult<string>, NoResultOperationResult>>
{
    public Results<OkResult<string>, NoResultOperationResult> Handle()
    {
        return new OkResult<string>("OK");
    }
}

public partial class ExplicitSemanticResultQueryOp : IQuery<OkResult<string>>
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("OK");
    }
}
