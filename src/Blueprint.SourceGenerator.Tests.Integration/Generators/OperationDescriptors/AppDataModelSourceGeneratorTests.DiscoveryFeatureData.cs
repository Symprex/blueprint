using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;

public class AppDataModelSourceGeneratorDiscoveryFeatureDataTests
{
    [Fact]
    public void When_IOperation_then_Configure_sets_operation_kind_feature_data()
    {
        var data = DiscoveryBareOperation.Configure().GetFeatureData<OperationDiscoveryFeatureData>();

        data.Should().Be(new OperationDiscoveryFeatureData(
            "operation",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors",
            nameof(DiscoveryBareOperation)));
    }

    [Fact]
    public void When_ICommand_then_Configure_sets_command_kind_feature_data()
    {
        var descriptor = DiscoveryWriteCommand.Configure();

        descriptor.TryGetFeatureData<OperationDiscoveryFeatureData>(out var data).Should().BeTrue();
        data.Should().Be(new OperationDiscoveryFeatureData(
            "command",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors",
            nameof(DiscoveryWriteCommand)));
    }

    [Fact]
    public void When_IQuery_then_Configure_sets_query_kind_feature_data()
    {
        var data = DiscoveryQueryOp.Configure().GetFeatureData<OperationDiscoveryFeatureData>();

        data.Should().Be(new OperationDiscoveryFeatureData(
            "query",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors",
            nameof(DiscoveryQueryOp)));
    }
}

public partial class DiscoveryBareOperation : IOperation
{
    public OkResult Handle()
    {
        return new("OK");
    }
}

// Not "DiscoveryCommandOp": legacy removed "Command" anywhere in a name (ApiOperationDescriptor.cs:50), which would
// name it "DiscoveryOp", as DiscoveryQueryOp is, and fail the build with BP009.
public partial class DiscoveryWriteCommand : ICommand
{
    public OkResult Handle()
    {
        return new("OK");
    }
}

public partial class DiscoveryQueryOp : IQuery<string>
{
    public string Handle()
    {
        return "OK";
    }
}
