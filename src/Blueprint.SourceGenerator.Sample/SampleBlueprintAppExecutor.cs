using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Api;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample;

public class SampleBlueprintAppExecutor : BlueprintAppExecutor
{
    public SampleBlueprintAppExecutor()
        : base(new SampleBlueprintApp())
    {
        this.Register(typeof(GetTemplateInlineQuery), new DelegatePipeline(c => Task.FromResult<OperationResult>(new OkResult(((GetTemplateInlineQuery)c.Operation).Handle()))));
        this.Register(typeof(GetTemplatesQuery), new DelegatePipeline(c => Task.FromResult<OperationResult>(new OkResult(new GetTemplatesQueryHandler().HandleAsync((GetTemplatesQuery)c.Operation)))));
    }
}


