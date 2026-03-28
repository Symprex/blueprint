using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelHostExposureTests
{
    [Fact]
    public void When_registered_operations_are_examined_then_http_host_index_contains_only_http_exposed_operations()
    {
        var app = new IntegrationTestBlueprintApp();

        var httpOperations = app.GetOperationsForHost("http");

        httpOperations.Should().ContainSingle(static operation => operation.OperationType == typeof(HttpExposedOperationToScan));
        httpOperations.Should().NotContain(static operation => operation.OperationType == typeof(InternalOnlyOperationToScan));

        var hostExposure = app.FindOperation(typeof(HttpExposedOperationToScan))
            .GetFeatureData<OperationHostExposureFeatureData>();

        hostExposure.Should().NotBeNull();
        hostExposure!.HostNames
            .Should()
            .Equal("http");

        app.FindOperation(typeof(InternalOnlyOperationToScan))
            .TryGetFeatureData<OperationHostExposureFeatureData>(out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void When_registered_operations_are_examined_then_tasks_host_index_contains_only_task_exposed_operations()
    {
        var app = new IntegrationTestBlueprintApp();

        var taskOperations = app.GetOperationsForHost("tasks");

        taskOperations.Should().ContainSingle(static operation => operation.OperationType == typeof(BackgroundTaskOperationToScan));
        taskOperations.Should().NotContain(static operation => operation.OperationType == typeof(HttpExposedOperationToScan));
        taskOperations.Should().NotContain(static operation => operation.OperationType == typeof(InternalOnlyOperationToScan));

        var hostExposure = app.FindOperation(typeof(BackgroundTaskOperationToScan))
            .GetFeatureData<OperationHostExposureFeatureData>();

        hostExposure.Should().NotBeNull();
        hostExposure!.HostNames
            .Should()
            .Equal("tasks");
    }
}

[Blueprint.Model.Attributes.RootLink("/host-aware/test")]
public partial class HttpExposedOperationToScan : IOperation
{
    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

public partial class InternalOnlyOperationToScan : IOperation
{
    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}

public partial class BackgroundTaskOperationToScan : IBackgroundTask
{
    public OkResult Handle()
    {
        return new OkResult("All OK");
    }
}
