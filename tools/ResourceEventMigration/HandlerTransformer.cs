using System.Text.RegularExpressions;

namespace ResourceEventMigration;

/// <summary>
/// Transforms command handler files to use the new ResourceEvent record pattern.
/// </summary>
public static class HandlerTransformer
{
    // Matches: ICommand<ResourceEvent<SomeType>>
    private static readonly Regex CommandInterfaceRegex = new(
        @"ICommand<ResourceEvent<(?<resource>[^>]+)>>",
        RegexOptions.Compiled);

    // Matches: Task<ResourceEvent<SomeType>>
    private static readonly Regex TaskReturnTypeRegex = new(
        @"Task<ResourceEvent<(?<resource>[^>]+)>>",
        RegexOptions.Compiled);

    // Matches: return EventsClass.EventName.New(arg);
    // Handles up to two levels of nested parens in args, multiline, and optional method chains
    private static readonly Regex NewCallRegex = new(
        @"return\s+(?<class>\w+)\.(?<event>\w+)\s*\.New\((?<args>(?:[^()]*|\((?:[^()]*|\([^()]*\))*\))*)\)(?<chain>[^;]*);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches: EventsClass.EventName.New(arg) (without return, e.g. in await expressions)
    private static readonly Regex NewCallExprRegex = new(
        @"(?<!\w)(?<class>\w+)\.(?<event>\w+)\s*\.New\((?<args>(?:[^()]*|\((?:[^()]*|\([^()]*\))*\))*)\)(?<chain>(?:\.\w+\([^)]*\))*)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Transforms a command/contract file to use the new return type.
    /// </summary>
    public static string TransformCommandInterface(string content, Dictionary<string, EventMapping> mappings)
    {
        // Replace ICommand<ResourceEvent<T>> with appropriate new return type
        content = CommandInterfaceRegex.Replace(content, match =>
        {
            var resource = match.Groups["resource"].Value;
            // Find the mapping for this resource type to determine SelfQuery vs body-bearing
            var mapping = mappings.Values.FirstOrDefault(m => m.Factory.ResourceType == resource);
            if (mapping != null)
            {
                var returnType = EventsFileGenerator.GetCommandReturnType(mapping.Factory);
                return $"ICommand<{returnType}>";
            }

            // Default to DeferredResourceEventResult (most common in S365)
            return $"ICommand<DeferredResourceEventResult<{resource}>>";
        });

        // Replace Task<ResourceEvent<T>> return types
        content = TaskReturnTypeRegex.Replace(content, match =>
        {
            var resource = match.Groups["resource"].Value;
            var mapping = mappings.Values.FirstOrDefault(m => m.Factory.ResourceType == resource);
            if (mapping != null)
            {
                var returnType = EventsFileGenerator.GetCommandReturnType(mapping.Factory);
                return $"Task<{returnType}>";
            }

            return $"Task<DeferredResourceEventResult<{resource}>>";
        });

        return content;
    }

    /// <summary>
    /// Transforms handler return statements from .New(domain) to new record construction.
    /// </summary>
    public static string TransformHandlerReturns(string content, Dictionary<string, EventMapping> mappings)
    {
        var usedMappings = new HashSet<string>(StringComparer.Ordinal);

        // Replace return statements: EventsClass.EventName.New(arg) → new NewClass.RecordName(constructedQuery)
        content = NewCallRegex.Replace(content, match =>
        {
            var className = match.Groups["class"].Value;
            var eventName = match.Groups["event"].Value;
            var args = match.Groups["args"].Value.Trim();
            var chain = match.Groups["chain"].Value.Trim();
            var key = $"{className}.{eventName}";

            if (mappings.TryGetValue(key, out var mapping))
            {
                usedMappings.Add(key);
                var replacement = BuildRecordConstruction(mapping, args);
                // Preserve method chains (e.g. .WithSecureData(...)) — need .ToOperationResult() first
                if (!string.IsNullOrEmpty(chain))
                {
                    return $"return {replacement}.ToOperationResult(){chain};";
                }

                return $"return {replacement};";
            }

            // If no mapping found, leave as-is with a comment
            return match.Value + " // TODO: migrate manually";
        });

        // Also replace non-return usages (e.g. in ternary, await, etc.)
        content = NewCallExprRegex.Replace(content, match =>
        {
            var className = match.Groups["class"].Value;
            var eventName = match.Groups["event"].Value;
            var args = match.Groups["args"].Value.Trim();
            var key = $"{className}.{eventName}";

            if (mappings.TryGetValue(key, out var mapping))
            {
                usedMappings.Add(key);
                return BuildRecordConstruction(mapping, args);
            }

            return match.Value;
        });

        // Add source usings from used mappings
        if (usedMappings.Count > 0)
        {
            var additionalUsings = usedMappings
                .Select(k => mappings[k])
                .SelectMany(static m => m.SourceUsings)
                .Where(static u => !u.StartsWith("Blueprint.Http"))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            content = AddSourceUsings(content, additionalUsings);
        }

        return content;
    }

    /// <summary>
    /// Adds source usings from Events files that the handler may now need (types referenced in inlined lambdas).
    /// Inserts before any using alias directives (SA1209 compliance).
    /// </summary>
    private static string AddSourceUsings(string content, List<string> usings)
    {
        var lines = content.Split('\n');
        int lastNonAliasUsingLine = -1;
        var existingUsings = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("using ") && trimmed.TrimEnd().EndsWith(";"))
            {
                var ns = trimmed["using ".Length..].TrimEnd().TrimEnd(';');
                existingUsings.Add(ns);

                // Only track position for non-alias usings (alias usings contain '=')
                if (!ns.Contains('='))
                {
                    lastNonAliasUsingLine = i;
                }
            }
        }

        var toAdd = usings.Where(u => !existingUsings.Contains(u)).ToList();
        if (toAdd.Count == 0 || lastNonAliasUsingLine < 0) return content;

        var insertText = string.Join("\n", toAdd.Select(static u => $"using {u};"));
        lines[lastNonAliasUsingLine] = lines[lastNonAliasUsingLine] + "\n" + insertText;
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Adds required using directives for the new types.
    /// Inserts before any using alias directives (SA1209 compliance).
    /// </summary>
    public static string AddRequiredUsings(string content)
    {
        var usingsToAdd = new List<string>();

        if ((content.Contains("DeferredResourceEventResult") || content.Contains("ResourceEventResult<")) &&
            !content.Contains("using Blueprint.Runtime.Exec;"))
        {
            usingsToAdd.Add("using Blueprint.Runtime.Exec;");
        }

        if (usingsToAdd.Count == 0) return content;

        // Find the last non-alias using directive
        var lines = content.Split('\n');
        int lastNonAliasUsingLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("using ") && trimmed.TrimEnd().EndsWith(";") && !trimmed.Contains('='))
            {
                lastNonAliasUsingLine = i;
            }
        }

        if (lastNonAliasUsingLine >= 0)
        {
            var insertText = string.Join("\n", usingsToAdd.Distinct());
            lines[lastNonAliasUsingLine] = lines[lastNonAliasUsingLine] + "\n" + insertText;
            content = string.Join("\n", lines);
        }

        return content;
    }

    /// <summary>
    /// Removes the using Blueprint.Http directive if no other Blueprint.Http types are used.
    /// </summary>
    public static string CleanupUsings(string content)
    {
        // Remove "using Blueprint.Http;" if the only usage was ResourceEvent/ResourceEventDefinition
        if (content.Contains("using Blueprint.Http;") &&
            !Regex.IsMatch(content, @"\bResourceEvent\b") &&
            !Regex.IsMatch(content, @"\bResourceEventDefinition\b") &&
            !Regex.IsMatch(content, @"\bIResourceEventAuditSink\b"))
        {
            content = Regex.Replace(content, @"using Blueprint\.Http;\s*\n", "");
        }

        return content;
    }

    /// <summary>
    /// Transforms .Data property access on resource event results to .Content (the new property name).
    /// Only applies to variables declared as ResourceEventResult or DeferredResourceEventResult.
    /// </summary>
    public static string TransformDataPropertyAccess(string content)
    {
        // Skip files that have Stripe event handling (they also use .Data)
        if (content.Contains("Stripe.") || content.Contains("using Stripe;"))
        {
            return content;
        }

        // Only apply in files that reference the new event result types
        if (!content.Contains("ResourceEventResult<") && !content.Contains("DeferredResourceEventResult<"))
        {
            return content;
        }

        // Replace .Data access on event result variables
        // Pattern: variableName.Data where variableName likely holds an event result
        content = Regex.Replace(content, @"(\w*[Ee]vent\w*)\.Data\b", "$1.Content");

        return content;
    }

    private static string BuildRecordConstruction(EventMapping mapping, string args)
    {
        var factory = mapping.Factory;

        switch (factory.Kind)
        {
            case FactoryKind.SelfQuery:
                if (factory.LambdaBody != null && factory.LambdaParameter != null)
                {
                    var queryConstruction = InlineLambda(factory.LambdaBody, factory.LambdaParameter, args, factory.DomainType);
                    return $"new {mapping.NewClassName}.{mapping.RecordName}({queryConstruction})";
                }

                // Singleton query or no parameter — query is pre-built
                return factory.LambdaBody != null
                    ? $"new {mapping.NewClassName}.{mapping.RecordName}({factory.LambdaBody})"
                    : $"new {mapping.NewClassName}.{mapping.RecordName}({args})";

            case FactoryKind.Mapper:
                if (factory.LambdaBody != null && factory.LambdaParameter != null)
                {
                    var mapperCall = InlineLambda(factory.LambdaBody, factory.LambdaParameter, args, factory.DomainType);
                    return $"new {mapping.NewClassName}.{mapping.RecordName}({mapperCall})";
                }

                return $"new {mapping.NewClassName}.{mapping.RecordName}({args})";

            case FactoryKind.NoMapper:
                return $"new {mapping.NewClassName}.{mapping.RecordName}({args})";

            default:
                return $"new {mapping.NewClassName}.{mapping.RecordName}({args})";
        }
    }

    /// <summary>
    /// Inlines a lambda body by replacing the parameter name with the actual argument.
    /// For tuple parameters, decomposes member access to positional elements of the argument tuple.
    /// </summary>
    private static string InlineLambda(string lambdaBody, string parameter, string actualArg, string? domainType)
    {
        // Handle tuple parameters: when domain type is a tuple like "(ApiId TenantId, string InvoiceNumber)"
        // and arg is a tuple literal like "(operation.TenantId, operation.Number)",
        // replace parameter.ElementName → positional expression from arg tuple.
        if (domainType != null && domainType.StartsWith("(") && domainType.EndsWith(")") &&
            actualArg.StartsWith("(") && actualArg.EndsWith(")"))
        {
            var tupleElementNames = ParseTupleElementNames(domainType);
            var tupleArgElements = ParseTupleLiteralElements(actualArg);

            if (tupleElementNames.Count > 0 && tupleArgElements.Count == tupleElementNames.Count)
            {
                var result = lambdaBody;
                for (int i = 0; i < tupleElementNames.Count; i++)
                {
                    result = Regex.Replace(
                        result,
                        $@"\b{Regex.Escape(parameter)}\.{Regex.Escape(tupleElementNames[i])}\b",
                        tupleArgElements[i].Trim());
                }

                return result;
            }
        }

        // Simple case: replace whole-word parameter with argument
        return Regex.Replace(lambdaBody, $@"\b{Regex.Escape(parameter)}\b", actualArg);
    }

    /// <summary>
    /// Parses tuple element names from a domain type like "(ApiId TenantId, string InvoiceNumber)".
    /// Returns ["TenantId", "InvoiceNumber"].
    /// </summary>
    private static List<string> ParseTupleElementNames(string tupleType)
    {
        var inner = tupleType[1..^1].Trim();
        var elements = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '(' or '<':
                    depth++;
                    break;
                case ')' or '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    var element = inner[start..i].Trim();
                    var name = ExtractLastIdentifier(element);
                    if (name != null) elements.Add(name);
                    start = i + 1;
                    break;
            }
        }

        var lastElement = inner[start..].Trim();
        var lastName = ExtractLastIdentifier(lastElement);
        if (lastName != null) elements.Add(lastName);

        return elements;
    }

    /// <summary>
    /// Extracts the last identifier from a tuple element declaration like "ApiId TenantId" → "TenantId".
    /// </summary>
    private static string? ExtractLastIdentifier(string element)
    {
        var parts = element.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }

    /// <summary>
    /// Parses top-level elements of a tuple literal, handling nested parentheses.
    /// Input: "(expr1, expr2)" → ["expr1", "expr2"]
    /// </summary>
    private static List<string> ParseTupleLiteralElements(string tupleLiteral)
    {
        var inner = tupleLiteral[1..^1].Trim();
        var elements = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '(' or '<':
                    depth++;
                    break;
                case ')' or '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    elements.Add(inner[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        elements.Add(inner[start..].Trim());
        return elements;
    }
}
