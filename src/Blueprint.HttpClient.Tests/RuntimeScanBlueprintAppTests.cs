using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves a contract assembly is scanned once per process, however many clients and hosts register against it.
/// </summary>
/// <remarks>
/// Every <c>AddBlueprintClient</c> call scanned its contract assembly again, reflecting over every exported type. A
/// consuming test suite that builds a host per test paid that for every client of every host - about a tenth of
/// Signature365's Portal test run. The scanned app is frozen, so one instance can serve every caller.
/// </remarks>
public sealed class RuntimeScanBlueprintAppTests
{
    [Fact]
    public void Scanning_the_same_assembly_twice_returns_the_same_frozen_app()
    {
        var assembly = typeof(SearchTemplatesQuery).Assembly;

        var first = RuntimeScanBlueprintApp.ScanAssembly(assembly);
        var second = RuntimeScanBlueprintApp.ScanAssembly(assembly);

        second.Should().BeSameAs(first);
    }
}
