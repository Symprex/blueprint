# Blueprint

Blueprint is a .NET framework for building HTTP APIs out of operations: small classes that each describe one
command or query. A Roslyn source generator turns them into compiled endpoint wiring, request binding, execution
pipelines, HATEOAS links and an OpenAPI document at build time. There is no reflection-driven discovery at start-up,
and mistakes in an API's shape fail the build with a diagnostic.

To build an API, reference `Blueprint.Runtime`, and add `Blueprint.SourceGenerator` as a compile-time dependency:

```xml
<PackageReference Include="Blueprint.Runtime" Version="x.y.z" />
<PackageReference Include="Blueprint.SourceGenerator" Version="x.y.z" PrivateAssets="all" />
```

The other `Blueprint.*` packages add an HTTP client, testing hosts, and integrations with Marten, Rebus and Hangfire.

- Getting started and how it works: https://github.com/Symprex/blueprint#readme
- Release notes: https://github.com/Symprex/blueprint/releases
- Licence: Apache 2.0. See https://github.com/Symprex/blueprint/blob/main/LICENSE
