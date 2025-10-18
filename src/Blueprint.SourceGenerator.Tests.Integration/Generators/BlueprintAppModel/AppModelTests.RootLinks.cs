using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelTests
{
    [Fact]
    public void When_operations_exist_then_registered()
    {
        var app = new IntegrationTestBlueprintApp();

        app.Operations.Should().NotBeEmpty();
        app.Operations.Should().Contain(o => o.OperationType == typeof(OperationToScan));
    }
}

public partial class OperationToScan : IOperation;
