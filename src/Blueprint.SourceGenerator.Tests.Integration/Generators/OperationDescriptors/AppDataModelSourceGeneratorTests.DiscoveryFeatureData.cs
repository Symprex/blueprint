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
        var descriptor = DiscoveryCommandOp.Configure();

        descriptor.TryGetFeatureData<OperationDiscoveryFeatureData>(out var data).Should().BeTrue();
        data.Should().Be(new OperationDiscoveryFeatureData(
            "command",
            "Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors",
            nameof(DiscoveryCommandOp)));
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

public partial class DiscoveryCommandOp : ICommand
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
