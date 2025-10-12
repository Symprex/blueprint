using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample;

public class DelegatePipeline : IOperationExecutorPipeline
{
    private readonly Func<ApiOperationContext, Task<OperationResult>> _execute;

    public DelegatePipeline(Func<ApiOperationContext, Task<OperationResult>> execute)
    {
        this._execute = execute;
    }

    public async Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        return await this._execute(context);
    }
}
