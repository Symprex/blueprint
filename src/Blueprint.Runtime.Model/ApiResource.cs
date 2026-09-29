using System.Text.Json.Serialization;
using Blueprint.Model.Attributes;

namespace Blueprint.Model;

/// <summary>
/// An ApiResource that can be returned from an operation that has a standard format
/// to be consumed by clients, including resource type ($object), links ($links) and whether this
/// represents a 'partial' response ($partial).
/// </summary>
public abstract class ApiResource : ILinkableResource, IHaveResourceKey
{
    private readonly Dictionary<string, Link> _links = new(5);

    /// <summary>
    /// The object type of this resource, used to indicate to clients what they
    /// are dealing with (i.e. 'user', 'account', 'group').
    /// </summary>
    /// <remarks>
    /// Override in derived classes to supply a static string for zero-allocation serialization;
    /// the default implementation derives the name at runtime from the concrete type via
    /// <see cref="GetTypeName"/>.
    /// Derived classes may also assign this property in their constructor to override the default.
    /// The summary is legacy Blueprint's, which the OpenAPI document describes the property with.
    /// </remarks>
    [JsonPropertyName("$object")]
    public virtual string Object
    {
        get => this._object ?? GetTypeName(this.GetType());
        protected set => this._object = value;
    }

    private string? _object;

    /// <summary>
    /// The links for this resource, other endpoints that apply to this resource
    /// in it's current state.
    /// </summary>
    [DoNotCompare]
    [JsonPropertyName("$links")]
    public IDictionary<string, Link> Links => this._links;

    /// <summary>
    /// Gets the stable key used when link generation needs to identify this resource instance in URLs.
    /// </summary>
    [JsonIgnore]
    public virtual string ResourceKey => null!;

    /// <summary>
    /// Gets or sets a value indicating whether or not this resource is 'partial', an indication that not all properties have been
    /// populated for performance reasons and a client should reload from the 'self' link to retrieve all values.
    /// </summary>
    [JsonPropertyName("$partial")]
    public bool IsPartial { get; set; }

    /// <summary>
    /// Returns whether a link with the given rel has already been added to this resource.
    /// </summary>
    /// <param name="rel">The relation to check.</param>
    /// <returns><see langword="true"/> if a link with the given rel exists; otherwise <see langword="false"/>.</returns>
    public bool HasLink(string rel)
    {
        return this._links.ContainsKey(rel);
    }

    /// <summary>
    /// Derives a conventional resource-type name from a CLR <paramref name="resourceType"/> by removing
    /// <c>ApiResource</c>, then <c>Resource</c>, then <c>DTO</c>, each wherever it occurs in the type's name
    /// (ordinal, case-sensitive), and camelizing what is left. Results are cached per type for zero-allocation
    /// repeated lookups.
    /// </summary>
    /// <remarks>
    /// This is legacy Blueprint's rule (three <c>Replace</c> calls and <c>Camelize</c>), so that the names of resource
    /// events (<c>eventId</c>, <c>resourceObject</c>) are the ones its clients were written against: a
    /// <c>FooApiResourceBar</c> is <c>fooBar</c>. Camelizing raises the character at the start and after each underscore,
    /// removing the underscore, then lowers the first character. A name that is nothing but markers, which legacy threw
    /// on, is camelized as it is.
    /// </remarks>
    /// <param name="resourceType">The CLR type to derive the name from.</param>
    /// <returns>A camelCase type name suitable for API serialization (e.g. <c>"tenant"</c> from <c>TenantApiResource</c>).</returns>
    public static string GetTypeName(Type resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        return _typeNameCache.GetOrAdd(resourceType, static t =>
        {
            var name = t.Name
                .Replace("ApiResource", string.Empty, StringComparison.Ordinal)
                .Replace("Resource", string.Empty, StringComparison.Ordinal)
                .Replace("DTO", string.Empty, StringComparison.Ordinal);

            var pascal = _pascalizeUnderscores.Replace(name.Length > 0 ? name : t.Name, static match => match.Groups[1].Value.ToUpperInvariant());

            return char.ToLowerInvariant(pascal[0]) + pascal[1..];
        });
    }

    private static readonly System.Text.RegularExpressions.Regex _pascalizeUnderscores = new("(?:^|_)(.)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> _typeNameCache = new();

    /// <summary>
    /// Adds a new link with the specific rel (relation) and <see cref="Link"/>
    /// definition, throwing an exception if a link with the specific relation already
    /// exists.
    /// </summary>
    /// <param name="rel">The relation the link has to this resource.</param>
    /// <param name="link">The link to be added.</param>
    /// <exception cref="InvalidOperationException">If a link with the same relation type has already been added.</exception>
    public void AddLink(string rel, Link link)
    {
        if (this._links.TryGetValue(rel, out var existing))
        {
            // Check if they are equivalent and bail if so, just make this operation idempotent and
            // do not throw exception
            if (existing == link)
            {
                return;
            }

            throw new InvalidOperationException(
                $@"A link with the rel '{rel}' (link href is {link.Href}) has already been added to the API resource of type '{this.GetType().FullName}'. Existing link has href of {existing.Href}.

Ensure that when adding links that no two exist for the same resource type (including the root) with the same rel value.");
        }

        // Allow exception to bubble if link already exists. Allow normal success path to be quicker instead
        // of checking for duplicates (could do so in tests?)
        this._links.Add(rel, link);
    }
}
