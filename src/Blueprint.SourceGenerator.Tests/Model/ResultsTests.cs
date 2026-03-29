using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

public class ResultsTests
{
    [Fact]
    public void Implicit_conversion_from_first_member_sets_first_union_accessors()
    {
        Results<SuccessResult, FailureResult> result = new SuccessResult("ok");

        result.IsT1.Should().BeTrue();
        result.IsT2.Should().BeFalse();
        result.AsT1.Should().Be(new SuccessResult("ok"));
    }

    [Fact]
    public void Implicit_conversion_from_second_member_sets_second_union_accessors()
    {
        Results<SuccessResult, FailureResult> result = new FailureResult("bad-request");

        result.IsT1.Should().BeFalse();
        result.IsT2.Should().BeTrue();
        result.AsT2.Should().Be(new FailureResult("bad-request"));
    }

    [Fact]
    public void Accessing_the_wrong_union_member_throws_a_helpful_exception()
    {
        Results<SuccessResult, FailureResult> result = new FailureResult("bad-request");

        var act = () => _ = result.AsT1;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("The current union value is Blueprint.SourceGenerator.Tests.Model.ResultsTests+FailureResult, not Blueprint.SourceGenerator.Tests.Model.ResultsTests+SuccessResult.");
    }

    [Fact]
    public void Accessing_a_default_union_member_throws_a_helpful_exception()
    {
        var result = default(Results<SuccessResult, FailureResult>);

        var act = () => _ = result.AsT1;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("The current union value has not been set.");
    }

    private sealed record SuccessResult(string Message);

    private sealed record FailureResult(string Reason);
}
