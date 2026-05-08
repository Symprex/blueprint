using Blueprint.Authorization;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves the <see cref="ExecutionAllowed"/> struct provides the correct factory semantics
/// for authorization check results, matching legacy Blueprint behavior.
/// </summary>
public class ExecutionAllowedTests
{
    [Fact]
    public void Yes_is_allowed()
    {
        var result = ExecutionAllowed.Yes;

        result.IsAllowed.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.Message.Should().BeNull();
        result.FailureType.Should().BeNull();
    }

    [Fact]
    public async Task YesTask_completes_synchronously_with_allowed_result()
    {
        var task = ExecutionAllowed.YesTask;

        task.IsCompletedSuccessfully.Should().BeTrue();
        var result = await task;
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void No_carries_reason_message_and_failure_type()
    {
        var result = ExecutionAllowed.No(
            "missing_scope",
            "You do not have permission to access this resource.",
            ExecutionAllowedFailureType.Authorisation);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("missing_scope");
        result.Message.Should().Be("You do not have permission to access this resource.");
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
    }

    [Fact]
    public void No_with_authentication_failure_type()
    {
        var result = ExecutionAllowed.No(
            "token_expired",
            "Your session has expired.",
            ExecutionAllowedFailureType.Authentication);

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authentication);
    }

    [Fact]
    public void ForbiddenException_from_ExecutionAllowed_preserves_failure_reason()
    {
        var denial = ExecutionAllowed.No(
            "insufficient_permissions",
            "Admin access required.",
            ExecutionAllowedFailureType.Authorisation);

        var exception = new ForbiddenException(denial);

        exception.HttpStatus.Should().Be(403);
        exception.FailureReason.Should().NotBeNull();
        exception.FailureReason!.Value.IsAllowed.Should().BeFalse();
        exception.FailureReason!.Value.Reason.Should().Be("insufficient_permissions");
        exception.FailureReason!.Value.Message.Should().Be("Admin access required.");
    }

    [Fact]
    public void ForbiddenException_from_string_has_no_failure_reason()
    {
        var exception = new ForbiddenException("Access denied");

        exception.HttpStatus.Should().Be(403);
        exception.FailureReason.Should().BeNull();
    }
}
