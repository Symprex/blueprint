using System.Collections;

using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Expansion;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates the <see cref="IResourceExpander"/> pattern by expanding
/// <see cref="OwnerApiResource"/> references on any resource that has an
/// <c>Expandable&lt;OwnerApiResource&gt;</c> property. This is the compile-time
/// equivalent of Signature365's <c>AzureAdGroupApiResourceExpander</c>.
/// </summary>
/// <remarks>
/// <para>
/// The generated pipeline discovers <c>Expandable&lt;OwnerApiResource&gt;</c> properties
/// at source-generation time and emits typed expansion calls. At runtime, when the client
/// passes <c>?expand=Owner</c>, the pipeline resolves this expander and calls
/// <see cref="ExpandAsync"/> with the unexpanded <c>Expandable</c> instances.
/// </para>
/// <para>
/// In a real consumer, this would call a user service or database to load user details.
/// The sample implementation returns hardcoded data to prove the end-to-end flow.
/// </para>
/// </remarks>
public sealed class OwnerResourceExpander : IResourceExpander
{
    /// <inheritdoc />
    public bool CanExpand(Type type)
    {
        return type == typeof(OwnerApiResource);
    }

    /// <inheritdoc />
    public ValueTask ExpandAsync(
        ApiOperationContext context,
        object response,
        IEnumerable entities,
        CancellationToken cancellationToken)
    {
        foreach (var entity in entities)
        {
            if (entity is not Model.Expandable<OwnerApiResource> expandable)
            {
                continue;
            }

            // In production, batch-load from a user service by expandable.Id
            expandable.Value = new OwnerApiResource
            {
                Id = expandable.Id,
                DisplayName = $"User {expandable.Id}",
                Email = $"{expandable.Id}@example.com",
            };
        }

        return ValueTask.CompletedTask;
    }
}
