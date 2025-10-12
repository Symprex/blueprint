using Blueprint.Model;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample;

[BlueprintAppScan]
[BlueprintAppScan(FromAssemblyOf = typeof(GetTemplatesQuery))]
public partial class SampleBlueprintApp : BlueprintAppDataModel;
