using System.Text.Json.Serialization;
using Blueprint.Model.Attributes;
using Newtonsoft.Json;

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
    [JsonProperty(PropertyName = "$object")]
    [JsonPropertyName("$object")]
    public abstract string Object { get; }

    /// <summary>
    /// The links for this resource, other endpoints that apply to this resource
    /// in it's current state.
    /// </summary>
    [DoNotCompare]
    [JsonProperty(PropertyName = "$links")]
    [JsonPropertyName("$links")]
    public IDictionary<string, Link> Links => this._links;

    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public virtual string ResourceKey => null!;

    /// <summary>
    /// Gets or sets a value indicating whether or not this resource is 'partial', an indication that not all properties have been
    /// populated for performance reasons and a client should reload from the 'self' link to retrieve all values.
    /// </summary>
    [JsonProperty(PropertyName = "$partial")]
    [JsonPropertyName("$partial")]
    public bool IsPartial { get; set; }

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
