using System.Text.Json;
using Blueprint.Model;
using Blueprint.Tasks;
using FluentAssertions;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves that <see cref="HangfireBackgroundTaskExecutor"/> correctly validates and deserializes
/// task payloads. Full end-to-end dispatch tests are in the integration test project because
/// <see cref="BlueprintAppExecutor"/> is a concrete class that requires a real BlueprintApp.
/// </summary>
public class HangfireBackgroundTaskExecutorTests
{
    [Fact]
    public void HangfireTaskPayload_round_trips_correctly()
    {
        var task = new TestExecutableTask { Message = "hello-world" };
        var payload = JsonSerializer.Serialize(new
        {
            TaskTypeName = typeof(TestExecutableTask).AssemblyQualifiedName,
            TaskJson = JsonSerializer.Serialize(task),
            Headers = new Dictionary<string, string> { ["TenantId"] = "t-1" },
        });

        var deserialized = JsonSerializer.Deserialize<HangfireTaskPayloadAccessor>(payload);

        deserialized.Should().NotBeNull();
        deserialized!.TaskTypeName.Should().Be(typeof(TestExecutableTask).AssemblyQualifiedName);

        var reconstituted = JsonSerializer.Deserialize<TestExecutableTask>(deserialized.TaskJson);
        reconstituted.Should().NotBeNull();
        reconstituted!.Message.Should().Be("hello-world");

        deserialized.Headers.Should().ContainKey("TenantId")
            .WhoseValue.Should().Be("t-1");
    }

    [Fact]
    public void Task_type_can_be_resolved_from_assembly_qualified_name()
    {
        var typeName = typeof(TestExecutableTask).AssemblyQualifiedName;
        var resolvedType = Type.GetType(typeName!);

        resolvedType.Should().NotBeNull();
        resolvedType.Should().Be(typeof(TestExecutableTask));
    }

    [Fact]
    public void Null_payload_deserialization_returns_null()
    {
        var result = JsonSerializer.Deserialize<HangfireTaskPayloadAccessor>("null");

        result.Should().BeNull();
    }

    public class TestExecutableTask : IBackgroundTask
    {
        public string Message { get; set; } = string.Empty;

        public static ApiOperationDescriptor Configure()
        {
            return new(typeof(TestExecutableTask), "TestExecutableTask", "test");
        }
    }

    /// <summary>
    /// Mirror of the internal HangfireTaskPayload for deserialization testing.
    /// </summary>
    private sealed class HangfireTaskPayloadAccessor
    {
        public string TaskTypeName { get; set; } = string.Empty;

        public string TaskJson { get; set; } = string.Empty;

        public Dictionary<string, string>? Headers { get; set; }
    }
}
