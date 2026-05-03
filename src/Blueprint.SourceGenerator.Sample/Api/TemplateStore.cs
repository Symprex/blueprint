using System.Collections.Concurrent;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Thread-safe in-memory template store that demonstrates shared state across handlers
/// without requiring external infrastructure.
/// </summary>
public sealed class TemplateStore
{
    private readonly ConcurrentDictionary<string, TemplateApiResource> _templates = new(StringComparer.Ordinal);
    private int _nextId;

    /// <summary>
    /// Initializes the store with seed data so the sample app has content on first request.
    /// </summary>
    public TemplateStore()
    {
        this.Seed();
    }

    /// <summary>
    /// Gets all templates in the store.
    /// </summary>
    /// <returns>An array of all template resources currently stored.</returns>
    public TemplateApiResource[] GetAll()
    {
        return [.. this._templates.Values];
    }

    /// <summary>
    /// Gets a single template by identifier, or null if it does not exist.
    /// </summary>
    /// <param name="id">The template identifier to look up.</param>
    /// <returns>The matching template resource, or null when the identifier is not found.</returns>
    public TemplateApiResource? GetById(string id)
    {
        return this._templates.TryGetValue(id, out var template) ? template : null;
    }

    /// <summary>
    /// Creates a new template with a generated identifier and returns the resource.
    /// </summary>
    /// <param name="name">The display name for the new template.</param>
    /// <param name="htmlSource">The HTML source content.</param>
    /// <param name="plainTextSource">The plain text source content.</param>
    /// <returns>The newly created template resource with a generated identifier.</returns>
    public TemplateApiResource Create(string name, string htmlSource, string plainTextSource)
    {
        var id = $"template-{Interlocked.Increment(ref this._nextId):D3}";
        var template = new TemplateApiResource(id, name, htmlSource, plainTextSource);
        this._templates[id] = template;
        return template;
    }

    /// <summary>
    /// Updates an existing template, returning the updated resource or null if not found.
    /// </summary>
    /// <param name="id">The identifier of the template to update.</param>
    /// <param name="name">The new display name.</param>
    /// <param name="htmlSource">The new HTML source content.</param>
    /// <param name="plainTextSource">The new plain text source content.</param>
    /// <returns>The updated template resource, or null when the identifier is not found.</returns>
    public TemplateApiResource? Update(string id, string name, string htmlSource, string plainTextSource)
    {
        if (!this._templates.ContainsKey(id))
        {
            return null;
        }

        var updated = new TemplateApiResource(id, name, htmlSource, plainTextSource);
        this._templates[id] = updated;
        return updated;
    }

    /// <summary>
    /// Deletes a template by identifier. This is idempotent — deleting a non-existent template is a no-op.
    /// </summary>
    /// <param name="id">The identifier of the template to delete.</param>
    public void Delete(string id)
    {
        this._templates.TryRemove(id, out _);
    }

    private void Seed()
    {
        this.Create("Welcome Email", "<p>Hello, {{Name}}!</p>", "Hello, {{Name}}!");
        this.Create("Formal Greeting", "<p>Dear {{Name}},</p>", "Dear {{Name}},");
    }
}
