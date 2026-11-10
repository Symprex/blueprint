using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.Mcp.Tests.App;

/// <summary>A query answering GET and POST, whose unannotated properties are bound from the body.</summary>
[AllowAnonymous]
[RootLink("/multi-method")]
[HttpGet]
[HttpPost]
public partial class MultiMethodEchoMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the name echoed.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the count echoed.</summary>
    public int Count { get; set; }

    /// <summary>Echoes the name and the count.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The name and the count, as <c>name:count</c>.</returns>
    public static string Handle(MultiMethodEchoMcpQuery query)
    {
        return $"{query.Name}:{query.Count}";
    }
}

/// <summary>A query that answers with the path of the request <see cref="IHttpContextAccessor"/> gives its handler.</summary>
[AllowAnonymous]
[RootLink("/accessor/{Id}")]
public partial class AccessorPathMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the route value.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>Handles <see cref="AccessorPathMcpQuery"/> from the ambient request.</summary>
/// <param name="accessor">The accessor the handler reads the request from.</param>
public class AccessorPathMcpQueryHandler(IHttpContextAccessor accessor)
{
    /// <summary>Answers with the ambient request's path.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The path of the request the accessor gives.</returns>
    public string Handle(AccessorPathMcpQuery query)
    {
        return accessor.HttpContext?.Request.Path.Value ?? "no request";
    }
}

/// <summary>
/// A query Blueprint lets anyone run, whose endpoint the tests' host guards with an ASP.NET Core policy instead.
/// </summary>
[AllowAnonymous]
[RootLink("/guarded")]
public partial class PolicyGuardedMcpQuery : IQuery<string>
{
    /// <summary>Answers once the policy has let the caller through.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A fixed answer.</returns>
    public static string Handle(PolicyGuardedMcpQuery query)
    {
        return "guarded";
    }
}

/// <summary>A query whose response is a binary file of the size asked for.</summary>
[AllowAnonymous]
[RootLink("/download")]
public partial class DownloadMcpQuery : IQuery<FileStreamResult>
{
    /// <summary>Gets or sets the file's size in bytes.</summary>
    public int Size { get; set; }

    /// <summary>Gives a file of zeros of the size asked for.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The file.</returns>
    public static FileStreamResult Handle(DownloadMcpQuery query)
    {
        return new FileStreamResult(new MemoryStream(new byte[query.Size]), "application/octet-stream");
    }
}
