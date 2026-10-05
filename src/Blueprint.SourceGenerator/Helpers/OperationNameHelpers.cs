using System.Text;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Shared helpers for normalising operation type names into descriptor names.
/// Both the per-operation descriptor generator and the app-wide registration
/// generator reference these helpers so the same naming rules are applied
/// consistently and collision detection is possible.
/// </summary>
/// <remarks>
/// The descriptor name belongs to the operation and is baked into its generated descriptor by
/// <see cref="GetDescriptorName"/>. An app's <c>[BlueprintAppOperationNames]</c> removal belongs to the app, because one
/// operation can be hosted by several apps, so it is applied at runtime by the app registration, to the baked name —
/// and <see cref="RemoveFromOperationName"/> is its compile-time mirror, which the collision checks (BP009, BP011) rely on
/// agreeing with that emitted code, so they must both be given the name <see cref="GetDescriptorName"/> produced.
/// </remarks>
internal static class OperationNameHelpers
{
    /// <summary>
    /// Gives the descriptor name of an operation type exactly as legacy Blueprint did:
    /// <c>apiOperationType.Name.Replace("Query", string.Empty).Replace("Command", string.Empty).ToPascalCase()</c>
    /// (S365 <c>8b40ecc961</c>, <c>backend/src/Blueprint/Blueprint/ApiOperationDescriptor.cs:50</c>). So
    /// <c>GetSentEmailQuery</c> is <c>GetSentEmail</c>, but <c>GenerateVCardQuery</c> is <c>GeneratevCard</c> and
    /// <c>GetInvoicesV2Query</c> is <c>GetInvoicesv2</c>. The name flows to OpenAPI <c>operationId</c> values,
    /// request-body schema keys, <c>x-links</c> and anything reading <c>ApiOperationDescriptor.Name</c>, such as an
    /// authorisation manifest, so a consumer's clients and route guards depend on these exact characters.
    /// </summary>
    /// <param name="typeName">The simple (non-qualified) operation class name, as legacy's <c>Type.Name</c>.</param>
    /// <remarks>
    /// <para>
    /// Both words are replaced wherever they occur, <c>Query</c> first, not only as a suffix; <c>BackgroundTask</c> is
    /// not touched. The casing then runs on the result (<see cref="ToLegacyPascalCase"/>).
    /// </para>
    /// <para>
    /// One deliberate departure: legacy left a type named only <c>Query</c>, <c>Command</c> or <c>QueryCommand</c> with an
    /// empty name, which is no usable operationId; such a type keeps its own name, cased.
    /// </para>
    /// </remarks>
    /// <returns>The descriptor name.</returns>
    internal static string GetDescriptorName(string typeName)
    {
        var replaced = typeName
            .Replace("Query", string.Empty, StringComparison.Ordinal)
            .Replace("Command", string.Empty, StringComparison.Ordinal);

        return ToLegacyPascalCase(replaced.Length == 0 ? typeName : replaced);
    }

    /// <summary>
    /// Ports legacy Blueprint's <c>StringExtensions.ToPascalCase</c> (S365 <c>8b40ecc961</c>,
    /// <c>backend/src/Blueprint/Blueprint/Utilities/StringExtensions.cs:65-120</c>) character for character: a space,
    /// <c>-</c> or <c>_</c> is dropped and upper-cases the next character; a digit is kept and upper-cases the next
    /// character; the first character is upper-cased; after that a letter keeps its case only when the next character is
    /// lower-case, or when it is <c>A</c> or <c>I</c> and the previous character is lower-case, and is otherwise
    /// lower-cased. So acronyms fold: <c>ABCAcronym</c> is <c>AbcAcronym</c> and <c>GetItemsETag</c> is
    /// <c>GetItemseTag</c>.
    /// </summary>
    /// <param name="value">The text to case.</param>
    /// <remarks>
    /// The neighbours examined are the input's, not the output's, as legacy's were. Legacy called the current-culture
    /// <c>char.ToUpper</c>/<c>char.ToLower</c>; this uses the invariant forms so a build's output cannot depend on the
    /// build machine's culture. The two agree for every culture except those with special casing rules, such as Turkish
    /// dotted and dotless I.
    /// </remarks>
    /// <returns>The cased text.</returns>
    internal static string ToLegacyPascalCase(string value)
    {
        var builder = new StringBuilder(value.Length);
        var toUpper = true;

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            switch (current)
            {
                case ' ':
                case '-':
                case '_':
                    toUpper = true;
                    continue;

                case >= '0' and <= '9':
                    toUpper = true;
                    builder.Append(current);
                    continue;

                default:
                    if (toUpper)
                    {
                        builder.Append(char.ToUpperInvariant(current));
                        toUpper = false;
                    }
                    else
                    {
                        var nextIsLower = i < value.Length - 1 && char.IsLower(value[i + 1]);
                        var previousIsLower = i > 0 && char.IsLower(value[i - 1]);

                        if (nextIsLower || (previousIsLower && (current == 'A' || current == 'I')))
                        {
                            builder.Append(current);
                        }
                        else
                        {
                            builder.Append(char.ToLowerInvariant(current));
                        }
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Gives the descriptor name an app registers once its <c>[BlueprintAppOperationNames(Remove = ...)]</c> has been
    /// applied: every ordinal occurrence of <paramref name="removal"/> is removed from a name that
    /// <see cref="GetDescriptorName"/> has already produced. That order and those semantics are legacy Blueprint's
    /// <c>descriptor.Name.Replace(prefix, string.Empty)</c> convention, which ran on the constructed descriptor's name
    /// (S365 <c>8b40ecc961</c>, <c>Blueprint/Configuration/OperationScanner.cs:286-303</c>), so on the cased name.
    /// </summary>
    /// <param name="descriptorName">The descriptor name <see cref="GetDescriptorName"/> gave the operation.</param>
    /// <param name="removal">The text the app removes; an empty value leaves the name unchanged.</param>
    /// <returns>The name the app registers the operation under.</returns>
    internal static string RemoveFromOperationName(string descriptorName, string removal)
    {
        return removal.Length == 0
            ? descriptorName
            : descriptorName.Replace(removal, string.Empty, StringComparison.Ordinal);
    }
}
