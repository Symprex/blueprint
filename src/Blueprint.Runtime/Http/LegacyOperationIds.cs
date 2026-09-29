namespace Blueprint.Runtime.Http;

/// <summary>
/// Gives every path and method of a rendered document the <c>operationId</c> legacy Blueprint's document gave it, which is
/// the operation's name made unique as NSwag 14.2.0 made it when it wrote the document.
/// </summary>
/// <remarks>
/// <para>
/// Legacy set every route's <c>operationId</c> to its operation's name (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.OpenApi/OpenApiDocumentBuilder.cs:94-109</c>), so an operation at several URLs had one id at each.
/// NSwag's <c>OpenApiDocument.ToJson</c> then called <c>GenerateOperationIds</c>, which this ports step for step,
/// over the document's operations in path order and, within a path, method order:
/// </para>
/// <list type="number">
/// <item><description>in each group sharing an id, <c>All</c> is appended to every operation with a collection success
/// response, unless every operation in the group has one;</description></item>
/// <item><description>then, in each group whose operations do not all share a method, the upper-cased method is appended
/// to each;</description></item>
/// <item><description>then the first group still shared is numbered, the first keeping its id and the rest taking
/// <c>2</c>, <c>3</c>, ..., and the whole rule runs again.</description></item>
/// </list>
/// <para>
/// Legacy's path order was the order its builder first reached each path: its operations in registration order, each
/// operation's links in declaration order (<c>Blueprint/Configuration/OperationScanner.cs:294-298</c>), and a path already
/// reached keeping its place. The document's operations stand in for legacy's registration order, which a generated app
/// does not have, so the order is exact for the operations of any one group, which in a generated app is always one
/// operation at several URLs (<c>BP009</c> and <c>BP011</c> keep operation names unique).
/// </para>
/// <para>
/// Only the paths' <c>operationId</c>s change: a request body keeps its operation's schema and an <c>x-links</c> entry
/// keeps the operation's name, as legacy's did.
/// </para>
/// </remarks>
internal static class LegacyOperationIds
{
    /// <summary>
    /// Assigns the <c>operationId</c> of every path and method that the document writes an operation at.
    /// </summary>
    /// <param name="document">The document being written.</param>
    /// <returns>The <c>operationId</c> of each written slot, keyed by the path's URL format and the upper-cased method.</returns>
    internal static IReadOnlyDictionary<(string UrlFormat, string Method), string> Assign(OpenApiDocumentRenderingContext document)
    {
        var slots = new Dictionary<(string UrlFormat, string Method), OpenApiOperationModel>();

        foreach (var path in document.Paths)
        {
            foreach (var method in path.Methods)
            {
                if (!method.Operations.IsEmpty)
                {
                    slots[(path.UrlFormat, method.HttpMethod.ToUpperInvariant())] = method.Operations[0];
                }
            }
        }

        var entries = OrderAsLegacyReachedThem(document, slots);
        GenerateOperationIds(entries);

        return entries.ToDictionary(static e => (e.UrlFormat, e.Method), static e => e.OperationId);
    }

    /// <summary>
    /// Makes the ids of a document's operations unique as NSwag 14.2.0 did, for operations given in the document's path
    /// order.
    /// </summary>
    /// <param name="operations">Each operation's id, upper-cased method and whether it returns a collection.</param>
    /// <returns>The unique ids, in the same order.</returns>
    internal static IReadOnlyList<string> GenerateOperationIds(IReadOnlyList<(string OperationId, string Method, bool IsCollection)> operations)
    {
        var entries = operations.Select(static (o, i) => new Entry(i.ToString(System.Globalization.CultureInfo.InvariantCulture), o.Method, o.IsCollection) { OperationId = o.OperationId }).ToList();
        GenerateOperationIds(entries);

        return entries.Select(static e => e.OperationId).ToList();
    }

    private static List<Entry> OrderAsLegacyReachedThem(
        OpenApiDocumentRenderingContext document,
        Dictionary<(string UrlFormat, string Method), OpenApiOperationModel> slots)
    {
        var declaredUrlFormats = document.PathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.SchemaProjection.Document.Operations
            .GroupBy(static o => o.Operation.OperationType.FullName ?? o.Operation.OperationType.Name, StringComparer.Ordinal)
            .ToDictionary(
                static g => g.Key,
                static g => g.First().Operation.Urls.Select(static url => OpenApiOperationRenderingContext.ToPathKey(url.UrlFormat)).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        // Each path, in the order it was first reached, with its methods in the order they were first reached.
        var pathOrder = new List<string>();
        var methodsByPath = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Reach(string urlFormat, string method)
        {
            if (!methodsByPath.TryGetValue(urlFormat, out var methods))
            {
                methods = [];
                methodsByPath[urlFormat] = methods;
                pathOrder.Add(urlFormat);
            }

            if (!methods.Contains(method, StringComparer.Ordinal))
            {
                methods.Add(method);
            }
        }

        foreach (var operation in document.PathItemRendering.OperationRendering.Operations)
        {
            var urlFormats = declaredUrlFormats.TryGetValue(operation.OperationTypeName, out var declared)
                ? declared.Where(operation.UrlFormats.Contains).Concat(operation.UrlFormats.Except(declared, StringComparer.Ordinal))
                : operation.UrlFormats;

            foreach (var urlFormat in urlFormats)
            {
                foreach (var httpMethod in operation.HttpMethods)
                {
                    var method = httpMethod.ToUpperInvariant();

                    if (slots.TryGetValue((urlFormat, method), out var written)
                        && string.Equals(written.OperationTypeName, operation.OperationTypeName, StringComparison.Ordinal))
                    {
                        Reach(urlFormat, method);
                    }
                }
            }
        }

        // A slot no operation above reached keeps the document's own order, after them.
        foreach (var slot in slots.Keys)
        {
            Reach(slot.UrlFormat, slot.Method);
        }

        var entries = new List<Entry>(slots.Count);

        foreach (var urlFormat in pathOrder)
        {
            foreach (var method in methodsByPath[urlFormat])
            {
                var operation = slots[(urlFormat, method)];
                entries.Add(new Entry(urlFormat, method, IsCollection(operation)) { OperationId = operation.OperationName });
            }
        }

        return entries;
    }

    // NSwag 14.2.0 OpenApiDocument.GenerateOperationIds, whose ids are never missing here.
    private static void GenerateOperationIds(List<Entry> entries)
    {
        // 1: Append All to operations returning collections.
        foreach (var group in entries.GroupBy(static e => e.OperationId, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                // If they all return collections, All would not tell them apart.
                if (group.Count(static e => e.IsCollection) == group.Count())
                {
                    continue;
                }

                foreach (var entry in group.Where(static e => e.IsCollection))
                {
                    entry.OperationId += "All";
                }
            }
        }

        // 2: Append the method.
        foreach (var group in entries.GroupBy(static e => e.OperationId, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                if (group.Select(static e => e.Method).Distinct(StringComparer.Ordinal).Count() == 1)
                {
                    continue;
                }

                foreach (var entry in group)
                {
                    entry.OperationId += entry.Method;
                }
            }
        }

        // 3: Append numbers as a last resort, and start again.
        foreach (var group in entries.GroupBy(static e => e.OperationId, StringComparer.Ordinal))
        {
            var duplicates = group.ToList();

            if (duplicates.Count > 1)
            {
                var number = 2;

                foreach (var entry in duplicates.Skip(1))
                {
                    entry.OperationId += number++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                GenerateOperationIds(entries);
                return;
            }
        }
    }

    /// <summary>
    /// Whether NSwag took the operation to return a collection: a 2xx response whose first content's schema is an
    /// array (<c>HttpUtilities.IsSuccessStatusCode</c> and <c>Schema?.ActualSchema.Type == JsonObjectType.Array</c>).
    /// </summary>
    private static bool IsCollection(OpenApiOperationModel operation)
    {
        return operation.Responses.Any(static response =>
            response.StatusCode is >= 200 and <= 299
            && response.Content.FirstOrDefault() is { } content
            && content.Discriminator is null
            && !content.IsResourceEventEnvelope
            && (content.InlineSchema?.Type == "array"
                || content.SchemaComponents is [{ IsArrayWrapper: true }]));
    }

    private sealed class Entry(string urlFormat, string method, bool isCollection)
    {
        public string UrlFormat { get; } = urlFormat;

        public string Method { get; } = method;

        public bool IsCollection { get; } = isCollection;

        public required string OperationId { get; set; }
    }
}
