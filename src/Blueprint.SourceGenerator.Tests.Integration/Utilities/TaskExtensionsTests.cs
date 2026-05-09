using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class TaskExtensionsTests
{
    [Fact]
    public async Task ConvertToObject_Returns_Boxed_Value()
    {
        var task = Task.FromResult(42);

        var result = await task.ConvertToObject();

        result.Should().Be(42);
    }

    [Fact]
    public async Task ConvertToObject_Returns_Reference_Type()
    {
        var task = Task.FromResult("hello");

        var result = await task.ConvertToObject();

        result.Should().Be("hello");
    }
}
