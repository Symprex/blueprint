using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Blueprint.Model.Attributes;

namespace Blueprint.HttpClient;

/// <summary>
/// What a gateway operation's forwards apply, read once per gateway operation and app type from their
/// <see cref="UpstreamCredentialAttribute"/> and <see cref="TranslateErrorAttribute"/>s and cached, so a forward reads no
/// metadata.
/// </summary>
/// <remarks>
/// A hand-written forward has no generated record of its operation's attributes, so this reads them; generated forwards
/// (#69) are given the same choices at compile time instead.
/// </remarks>
internal sealed class UpstreamForwardPolicy
{
    private const DynamicallyAccessedMemberTypes FactoryMembers = DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.NonPublicProperties;

    private static readonly ConcurrentDictionary<(Type Operation, Type App), UpstreamForwardPolicy> _policies = new();

    private readonly Dictionary<string, ApiExceptionFactory> _translations;

    private UpstreamForwardPolicy(UpstreamCredentialAttribute? credentialAttribute, Dictionary<string, ApiExceptionFactory> translations)
    {
        this.CredentialAttribute = credentialAttribute;
        this._translations = translations;
    }

    /// <summary>
    /// Gets the attribute selecting the operation's credential strategy, or <see langword="null"/> when the operation
    /// forwards its caller's <c>Authorization</c> header.
    /// </summary>
    public UpstreamCredentialAttribute? CredentialAttribute { get; }

    /// <summary>
    /// Gets the policy for forwards from <paramref name="operationType"/> hosted by <paramref name="appType"/>.
    /// </summary>
    /// <param name="operationType">The gateway operation's type.</param>
    /// <param name="appType">The gateway app's type.</param>
    /// <returns>The policy, read on first use and cached.</returns>
    /// <exception cref="InvalidOperationException">
    /// The operation carries more than one <see cref="UpstreamCredentialAttribute"/>, or a <see cref="TranslateErrorAttribute"/>
    /// names a member that is not a static <see cref="ApiExceptionFactory"/>.
    /// </exception>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "A TranslateError target is a static member of the attributed class, which the gateway's own code declares and references.")]
    public static UpstreamForwardPolicy For(Type operationType, Type appType)
    {
        ArgumentNullException.ThrowIfNull(operationType);
        ArgumentNullException.ThrowIfNull(appType);

        return _policies.GetOrAdd((operationType, appType), static key => Create(key.Operation, key.App));
    }

    /// <summary>
    /// Finds the local factory an upstream problem of <paramref name="problemType"/> is answered with.
    /// </summary>
    /// <param name="problemType">The upstream problem's <c>type</c>.</param>
    /// <returns>The factory, or <see langword="null"/> when the problem passes through unchanged.</returns>
    public ApiExceptionFactory? FindTranslation(string? problemType)
    {
        return problemType is not null && this._translations.TryGetValue(problemType, out var factory) ? factory : null;
    }

    private static UpstreamForwardPolicy Create(
        [DynamicallyAccessedMembers(FactoryMembers)] Type operationType,
        [DynamicallyAccessedMembers(FactoryMembers)] Type appType)
    {
        var credentials = operationType.GetCustomAttributes<UpstreamCredentialAttribute>(inherit: false).ToList();

        if (credentials.Count > 1)
        {
            throw new InvalidOperationException(
                $"{operationType.Name} carries {credentials.Count} upstream credential attributes ({string.Join(", ", credentials.Select(c => c.GetType().Name))}); a gateway operation's forwards authenticate with one strategy.");
        }

        var translations = new Dictionary<string, ApiExceptionFactory>(StringComparer.Ordinal);

        // The app's first, so the operation's own translation of the same problem type replaces it.
        AddTranslations(appType, translations);
        AddTranslations(operationType, translations);

        return new UpstreamForwardPolicy(credentials.SingleOrDefault(), translations);
    }

    private static void AddTranslations([DynamicallyAccessedMembers(FactoryMembers)] Type declaringType, Dictionary<string, ApiExceptionFactory> translations)
    {
        foreach (var translation in declaringType.GetCustomAttributes<TranslateErrorAttribute>(inherit: false))
        {
            translations[translation.FromType] = ResolveFactory(declaringType, translation);
        }
    }

    private static ApiExceptionFactory ResolveFactory([DynamicallyAccessedMembers(FactoryMembers)] Type declaringType, TranslateErrorAttribute translation)
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var value = declaringType.GetField(translation.To, Static)?.GetValue(null)
            ?? declaringType.GetProperty(translation.To, Static)?.GetValue(null);

        return value as ApiExceptionFactory
            ?? throw new InvalidOperationException(
                $"[TranslateError(\"{translation.FromType}\", \"{translation.To}\")] on {declaringType.Name} names no static {nameof(ApiExceptionFactory)} field or property of {declaringType.Name} called '{translation.To}'.");
    }
}
