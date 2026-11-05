using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A consumer's operation deriving from a contract query, as a gateway operation does. It has a route of its own, as a
/// gateway operation does, which a client of the contract must not send it to.
/// </summary>
[RootLink("/gateway/templates/search")]
[HttpGet]
[AllowAnonymous]
public sealed partial class GatewaySearchTemplatesQuery : SearchTemplatesQuery;

/// <summary>A consumer's operation deriving from a contract command with a body.</summary>
[RootLink("/gateway/templates")]
[HttpPost]
[AllowAnonymous]
public sealed partial class GatewayCreateTemplateCommand : CreateTemplateCommand;

/// <summary>A consumer's operation deriving from a contract command bound from the route.</summary>
[RootLink("/gateway/templates/{Id}/archive")]
[HttpPost]
[AllowAnonymous]
public sealed partial class GatewayArchiveTemplateCommand : ArchiveTemplateCommand;

/// <summary>An operation that neither is nor derives from any operation a contract client's app registers.</summary>
[RootLink("/gateway/unregistered")]
[HttpPost]
[AllowAnonymous]
public sealed partial class UnregisteredOperation : ICommand;
