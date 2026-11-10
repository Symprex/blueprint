using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.Mcp.Tests.App.Lower;

/// <summary>One of two queries whose names differ only in case.</summary>
[AllowAnonymous]
[RootLink("/case/lower")]
public partial class GetfooMcpQuery : IQuery<string>
{
    /// <summary>Answers which of the two it is.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A fixed answer.</returns>
    public static string Handle(GetfooMcpQuery query)
    {
        return "lower";
    }
}
