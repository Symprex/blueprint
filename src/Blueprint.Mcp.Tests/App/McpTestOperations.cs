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

/// <summary>A query that answers with the subject of the client certificate its request's connection carries.</summary>
[AllowAnonymous]
[RootLink("/client-certificate")]
public partial class ClientCertificateMcpQuery : IQuery<string>
{
}

/// <summary>Handles <see cref="ClientCertificateMcpQuery"/> from the ambient request's connection.</summary>
/// <param name="accessor">The accessor the handler reads the connection from.</param>
public class ClientCertificateMcpQueryHandler(IHttpContextAccessor accessor)
{
    /// <summary>Answers with the client certificate's subject.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The subject, or <c>none</c>.</returns>
    public string Handle(ClientCertificateMcpQuery query)
    {
        return accessor.HttpContext?.Connection.ClientCertificate?.Subject ?? "none";
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

/// <summary>
/// A query Blueprint lets anyone run, whose endpoint the tests' host guards with an authorisation requirement an
/// attribute supplies instead.
/// </summary>
[AllowAnonymous]
[RootLink("/requirement")]
public partial class RequirementGuardedMcpQuery : IQuery<string>
{
    /// <summary>Answers once the requirement has let the caller through.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A fixed answer.</returns>
    public static string Handle(RequirementGuardedMcpQuery query)
    {
        return "required";
    }
}

/// <summary>A query whose handler changes its response's status as it starts and registers a resource for disposal.</summary>
[AllowAnonymous]
[RootLink("/callbacks")]
public partial class CallbacksMcpQuery : IQuery<string>
{
}

/// <summary>Handles <see cref="CallbacksMcpQuery"/> through the ambient response.</summary>
/// <param name="accessor">The accessor the handler reads the response from.</param>
/// <param name="probe">Counts the resources disposed.</param>
public class CallbacksMcpQueryHandler(IHttpContextAccessor accessor, ResponseCallbackProbe probe)
{
    /// <summary>Registers a callback that sets the status to 203 and a resource the probe counts on disposal.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A fixed answer.</returns>
    public string Handle(CallbacksMcpQuery query)
    {
        var response = accessor.HttpContext!.Response;

        response.OnStarting(() =>
        {
            response.StatusCode = StatusCodes.Status203NonAuthoritative;
            return Task.CompletedTask;
        });
        response.RegisterForDispose(probe.CreateResource());
        response.OnCompleted(() =>
        {
            probe.CompletedPath = accessor.HttpContext?.Request.Path.Value;
            return Task.CompletedTask;
        });

        return "called back";
    }
}

/// <summary>Counts the resources <see cref="CallbacksMcpQueryHandler"/> registered that have been disposed.</summary>
public sealed class ResponseCallbackProbe
{
    private int _disposed;

    /// <summary>Gets the number of resources disposed.</summary>
    public int Disposed => Volatile.Read(ref this._disposed);

    /// <summary>Gets or sets the request path the accessor gave the last <c>OnCompleted</c> callback.</summary>
    public string? CompletedPath { get; set; }

    /// <summary>Makes a resource that counts itself when disposed.</summary>
    /// <returns>The resource.</returns>
    public IDisposable CreateResource()
    {
        return new Resource(this);
    }

    private sealed class Resource(ResponseCallbackProbe probe) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Increment(ref probe._disposed);
        }
    }
}

/// <summary>A query answering HEAD only, whose handler still gives a body, which a server does not send.</summary>
[AllowAnonymous]
[RootLink("/head")]
[HttpMethod("HEAD")]
public partial class HeadMcpQuery : IQuery<string>
{
    /// <summary>Gives a body a HEAD response does not carry.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A fixed answer.</returns>
    public static string Handle(HeadMcpQuery query)
    {
        return "a body HEAD does not carry";
    }
}

/// <summary>A query answering with the roles of the user its request is authenticated as.</summary>
[AllowAnonymous]
[RootLink("/who-am-i")]
public partial class WhoAmIMcpQuery : IQuery<string>
{
}

/// <summary>Handles <see cref="WhoAmIMcpQuery"/> from the ambient request's user.</summary>
/// <param name="accessor">The accessor the handler reads the user from.</param>
public class WhoAmIMcpQueryHandler(IHttpContextAccessor accessor)
{
    /// <summary>Answers with the user's roles.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The roles, comma-separated, or <c>anonymous</c>.</returns>
    public string Handle(WhoAmIMcpQuery query)
    {
        var user = accessor.HttpContext?.User;

        return user?.Identity?.IsAuthenticated == true
            ? string.Join(',', user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(static c => c.Value))
            : "anonymous";
    }
}

/// <summary>A query answering with its route value, as the operation was given it.</summary>
[AllowAnonymous]
[RootLink("/echo/{Id}")]
public partial class EchoIdMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the route value.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Answers with the route value.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The route value.</returns>
    public static string Handle(EchoIdMcpQuery query)
    {
        return query.Id;
    }
}

/// <summary>A query answering with its catch-all route value, as the operation was given it.</summary>
[AllowAnonymous]
[RootLink("/folders/{**FolderPath}")]
public partial class FolderPathMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the folder path, every segment after <c>folders/</c>.</summary>
    public string? FolderPath { get; set; }

    /// <summary>Answers with the folder path.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The folder path, or <c>(none)</c>.</returns>
    public static string Handle(FolderPathMcpQuery query)
    {
        return query.FolderPath ?? "(none)";
    }
}

/// <summary>A query whose route value must be a positive integer, as its inline constraints say.</summary>
[AllowAnonymous]
[RootLink("/items/{Id:int:min(1)}")]
public partial class ConstrainedMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the item's id.</summary>
    public int Id { get; set; }

    /// <summary>Answers with the id.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The id.</returns>
    public static string Handle(ConstrainedMcpQuery query)
    {
        return query.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>A query at a parameterised route that a literal route (<see cref="OverlapSpecialMcpQuery"/>) overlaps.</summary>
[AllowAnonymous]
[RootLink("/overlap/{Name}")]
public partial class OverlapByNameMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Answers with the name, as this operation was given it.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The name.</returns>
    public static string Handle(OverlapByNameMcpQuery query)
    {
        return "by-name:" + query.Name;
    }
}

/// <summary>A query at a literal route, which HTTP routing prefers to <see cref="OverlapByNameMcpQuery"/>'s.</summary>
[AllowAnonymous]
[RootLink("/overlap/special")]
public partial class OverlapSpecialMcpQuery : IQuery<string>
{
    /// <summary>Answers as the literal route's operation.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A fixed answer.</returns>
    public static string Handle(OverlapSpecialMcpQuery query)
    {
        return "special";
    }
}

/// <summary>A query whose route value has a default, which routing supplies when the URL leaves it out.</summary>
[AllowAnonymous]
[RootLink("/defaulted/{Kind=all}")]
public partial class DefaultedMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the kind.</summary>
    public string? Kind { get; set; }

    /// <summary>Answers with the kind.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The kind, or <c>(none)</c>.</returns>
    public static string Handle(DefaultedMcpQuery query)
    {
        return query.Kind ?? "(none)";
    }
}

/// <summary>A query whose route value is admitted by a constraint that reads the request: only a GET passes it.</summary>
[AllowAnonymous]
[RootLink("/request-aware/{Id:getonly}")]
public partial class RequestAwareConstraintMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the route value.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Answers with the route value.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The route value.</returns>
    public static string Handle(RequestAwareConstraintMcpQuery query)
    {
        return query.Id;
    }
}

/// <summary>A route constraint that matches only a GET request, whatever the value.</summary>
public sealed class GetOnlyRouteConstraint : IRouteConstraint
{
    /// <inheritdoc />
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        return httpContext is null || HttpMethods.IsGet(httpContext.Request.Method);
    }
}

/// <summary>A query whose route value is admitted by a constraint that reads the connection: only a loopback caller passes it.</summary>
[AllowAnonymous]
[RootLink("/connection-aware/{Id:loopbackonly}")]
public partial class ConnectionAwareConstraintMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the route value.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Answers with the route value.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The route value.</returns>
    public static string Handle(ConnectionAwareConstraintMcpQuery query)
    {
        return query.Id;
    }
}

/// <summary>A route constraint that matches only a request from a loopback address, whatever the value.</summary>
public sealed class LoopbackOnlyRouteConstraint : IRouteConstraint
{
    /// <inheritdoc />
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        return httpContext is null
               || (httpContext.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address));
    }
}

/// <summary>A query whose route value is admitted by a constraint that reads the user: only a signed-in caller passes it.</summary>
[AllowAnonymous]
[RootLink("/user-aware/{Id:signedinonly}")]
public partial class UserAwareConstraintMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the route value.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Answers with the route value.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The route value.</returns>
    public static string Handle(UserAwareConstraintMcpQuery query)
    {
        return query.Id;
    }
}

/// <summary>
/// A route constraint that matches only a request whose user is signed in, whatever the value, as one may in a host that
/// authenticates before it routes.
/// </summary>
public sealed class SignedInOnlyRouteConstraint : IRouteConstraint
{
    /// <inheritdoc />
    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        return httpContext is null || httpContext.User.Identity?.IsAuthenticated == true;
    }
}

/// <summary>A query whose one route segment holds two values, which routing splits at the last hyphen it can.</summary>
[AllowAnonymous]
[RootLink("/range/{From}-{To}")]
public partial class RangeMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the start of the range.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets the end of the range.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Answers with the two values as the operation was given them.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The values, as <c>from|to</c>.</returns>
    public static string Handle(RangeMcpQuery query)
    {
        return $"{query.From}|{query.To}";
    }
}

/// <summary>A query whose route segment ends in an optional value behind a separator, which routing lets a URL leave out.</summary>
[AllowAnonymous]
[RootLink("/files/{Name}.{Ext?}")]
public partial class FileNameMcpQuery : IQuery<string>
{
    /// <summary>Gets or sets the file's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the file's extension, if the URL gives one.</summary>
    public string? Ext { get; set; }

    /// <summary>Answers with the name and extension as the operation was given them.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The name and extension, as <c>name|ext</c>.</returns>
    public static string Handle(FileNameMcpQuery query)
    {
        return $"{query.Name}|{query.Ext ?? "(none)"}";
    }
}

/// <summary>
/// The caller's tenant, a scoped service the host's middleware sets from the signed-in user, as a host following the
/// scoped-tenant pattern sets its tenant accessor.
/// </summary>
public sealed class CurrentTenant
{
    /// <summary>Gets or sets the tenant, or <see langword="null"/> where no middleware has set it.</summary>
    public string? Id { get; set; }
}

/// <summary>A query answering with the tenant the host's middleware set on its request's scope.</summary>
[AllowAnonymous]
[RootLink("/current-tenant")]
public partial class CurrentTenantMcpQuery : IQuery<string>
{
}

/// <summary>Handles <see cref="CurrentTenantMcpQuery"/> from the request's <see cref="CurrentTenant"/>.</summary>
/// <param name="tenant">The request's tenant.</param>
public class CurrentTenantMcpQueryHandler(CurrentTenant tenant)
{
    /// <summary>Answers with the tenant.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The tenant, or <c>none</c>.</returns>
    public string Handle(CurrentTenantMcpQuery query)
    {
        return tenant.Id ?? "none";
    }
}
