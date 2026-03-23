using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample;

/// <summary>
/// Wraps a delegate in the pipeline interface so the sample app can demonstrate execution without relying on generated
/// infrastructure in every example.
/// </summary>
public class DelegatePipeline : IOperationExecutorPipeline
{
    private readonly Func<ApiOperationContext, Task<OperationResult>> _execute;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegatePipeline"/> class.
    /// </summary>
    /// <param name="execute">The delegate that should be invoked when the sample pipeline executes.</param>
    public DelegatePipeline(Func<ApiOperationContext, Task<OperationResult>> execute)
    {
        this._execute = execute;
    }

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        return await this._execute(context);
    }
}
