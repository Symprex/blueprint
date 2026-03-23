using Blueprint.Model;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample;

/// <summary>
/// Sample Blueprint app used to demonstrate scan-based registration against both local and contract-assembly operations.
/// </summary>
[BlueprintAppScan]
[BlueprintAppScan(FromAssemblyOf = typeof(GetTemplatesQuery))]
public partial class SampleBlueprintApp : BlueprintApp;
