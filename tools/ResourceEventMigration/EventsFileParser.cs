using System.Text.RegularExpressions;

namespace ResourceEventMigration;

/// <summary>
/// Parses *Events.cs files to extract factory and event definitions using regex-based analysis.
/// </summary>
public static class EventsFileParser
{
    // Matches factory field: private static readonly XXX _fieldName = ...
    private static readonly Regex FactoryFieldRegex = new(
        @"private\s+static\s+readonly\s+\w+(?:<[^>]+>)?\s+(?<name>_\w+)\s*=",
        RegexOptions.Compiled);

    // Matches event definition: public static readonly XXX EventName = _factory.Method("subId");
    private static readonly Regex EventDefinitionRegex = new(
        @"public\s+static\s+readonly\s+\w+(?:<[^>]+>)?\s+(?<name>\w+)\s*=\s*(?<factory>_\w+)\.(?<method>Created|Updated|Deleted)\s*\(\s*(?:""(?<subId>[^""]+)"")?\s*\)\s*;",
        RegexOptions.Compiled);

    // Matches class declaration: public static class ClassName
    private static readonly Regex ClassDeclRegex = new(
        @"public\s+static\s+class\s+(?<name>\w+)",
        RegexOptions.Compiled);

    // Matches using directive
    private static readonly Regex UsingRegex = new(
        @"^using\s+(?<ns>[^;]+);",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Matches namespace declaration
    private static readonly Regex NamespaceRegex = new(
        @"namespace\s+(?<ns>[^;{]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses an *Events.cs file and returns all EventsClass definitions found within.
    /// A single file may contain multiple classes (e.g. UserEvents + AddInUserEvents).
    /// </summary>
    public static List<EventsClass> Parse(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var results = new List<EventsClass>();

        // Extract top-level info
        var usings = UsingRegex.Matches(content)
            .Select(m => m.Groups["ns"].Value.Trim())
            .ToList();

        var namespaceName = NamespaceRegex.Match(content).Groups["ns"].Value.Trim();

        // Split by class declarations
        var classMatches = ClassDeclRegex.Matches(content);
        for (int i = 0; i < classMatches.Count; i++)
        {
            var classMatch = classMatches[i];
            var className = classMatch.Groups["name"].Value;

            // Get the section of content for this class
            int startPos = classMatch.Index;
            int endPos = i + 1 < classMatches.Count ? classMatches[i + 1].Index : content.Length;
            var classContent = content[startPos..endPos];

            // Skip generic classes — they need manual migration
            if (classContent[classMatch.Length..].TrimStart().StartsWith("<"))
            {
                continue;
            }

            var factories = ParseFactories(classContent);
            var events = ParseEvents(classContent);

            results.Add(new EventsClass(
                className,
                namespaceName,
                filePath,
                usings,
                factories,
                events));
        }

        return results;
    }

    private static List<FactoryDefinition> ParseFactories(string classContent)
    {
        var factories = new List<FactoryDefinition>();

        var fieldMatches = FactoryFieldRegex.Matches(classContent);

        foreach (Match fieldMatch in fieldMatches)
        {
            var fieldName = fieldMatch.Groups["name"].Value;

            // Get the full statement for this field (find matching semicolon accounting for braces)
            int startIdx = fieldMatch.Index;
            var fullStatement = ExtractFullStatement(classContent, startIdx);
            if (fullStatement == null) continue;

            var factory = ParseFactoryStatement(fieldName, fullStatement);
            if (factory != null)
            {
                factories.Add(factory);
            }
        }

        return factories;
    }

    /// <summary>
    /// Extracts a full statement from the start index, handling multi-line with nested braces.
    /// </summary>
    private static string? ExtractFullStatement(string content, int startIdx)
    {
        int braceDepth = 0;
        int parenDepth = 0;
        bool inBraces = false;

        for (int i = startIdx; i < content.Length; i++)
        {
            switch (content[i])
            {
                case '{':
                    braceDepth++;
                    inBraces = true;
                    break;
                case '}':
                    braceDepth--;
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth--;
                    break;
                case ';' when braceDepth == 0 && parenDepth == 0:
                    return content[startIdx..(i + 1)];
                case ';' when braceDepth == 0 && inBraces:
                    // End of init after closing brace
                    return content[startIdx..(i + 1)];
            }
        }

        // Fallback: look for semicolons within reasonable range
        int semiIdx = content.IndexOf(';', startIdx);
        if (semiIdx >= 0 && semiIdx - startIdx < 2000)
        {
            return content[startIdx..(semiIdx + 1)];
        }

        return null;
    }

    private static FactoryDefinition? ParseFactoryStatement(string fieldName, string statement)
    {
        // Normalize whitespace for pattern matching
        var normalized = Regex.Replace(statement, @"\s+", " ").Trim();

        // Extract resource type from the field type declaration
        var resourceType = ExtractResourceTypeFromField(normalized);

        // Pattern 1: Singleton query — For(new GetSomeQuery())
        var singletonMatch = Regex.Match(normalized, @"ResourceEventDefinition\s*\.\s*For\s*\(\s*new\s+(?<query>\w+)\s*\(\s*\)\s*\)");
        if (singletonMatch.Success)
        {
            return new FactoryDefinition(
                fieldName,
                resourceType,
                null,
                FactoryKind.SelfQuery,
                singletonMatch.Groups["query"].Value,
                null,
                $"new {singletonMatch.Groups["query"].Value}()");
        }

        // Pattern 2: Generic For<TResource, TDomain>(param => new Query { ... })
        var genericSelfQueryMatch = Regex.Match(normalized,
            @"ResourceEventDefinition\s*\.\s*For<(?<resource>[^,>]+),\s*(?<domain>[^>]+)>\s*\(\s*(?<param>\w+)\s*=>\s*(?<body>new\s+.+?\})\s*\)",
            RegexOptions.Singleline);
        if (genericSelfQueryMatch.Success)
        {
            return new FactoryDefinition(
                fieldName,
                genericSelfQueryMatch.Groups["resource"].Value.Trim(),
                genericSelfQueryMatch.Groups["domain"].Value.Trim(),
                FactoryKind.SelfQuery,
                ExtractQueryType(genericSelfQueryMatch.Groups["body"].Value),
                genericSelfQueryMatch.Groups["param"].Value.Trim(),
                genericSelfQueryMatch.Groups["body"].Value.Trim());
        }

        // Pattern 3: Explicit typed param For((DomainType param) => new Query { ... })
        var explicitParamMatch = Regex.Match(normalized,
            @"ResourceEventDefinition\s*\.\s*For\s*\(\s*\((?<domain>[^)]+)\s+(?<param>\w+)\)\s*=>\s*(?<body>new\s+.+?\})\s*\)",
            RegexOptions.Singleline);
        if (explicitParamMatch.Success)
        {
            return new FactoryDefinition(
                fieldName,
                resourceType,
                explicitParamMatch.Groups["domain"].Value.Trim(),
                FactoryKind.SelfQuery,
                ExtractQueryType(explicitParamMatch.Groups["body"].Value),
                explicitParamMatch.Groups["param"].Value.Trim(),
                explicitParamMatch.Groups["body"].Value.Trim());
        }

        // Pattern 4: Tuple typed param For(((Type1, Type2) param) => new Query { ... })
        var tupleMatch = Regex.Match(normalized,
            @"ResourceEventDefinition\s*\.\s*For\s*\(\s*\(\((?<domain>[^)]+)\)\s+(?<param>\w+)\)\s*=>\s*(?<body>new\s+.+?\})\s*\)",
            RegexOptions.Singleline);
        if (tupleMatch.Success)
        {
            return new FactoryDefinition(
                fieldName,
                resourceType,
                "(" + tupleMatch.Groups["domain"].Value.Trim() + ")",
                FactoryKind.SelfQuery,
                ExtractQueryType(tupleMatch.Groups["body"].Value),
                tupleMatch.Groups["param"].Value.Trim(),
                tupleMatch.Groups["body"].Value.Trim());
        }

        // Pattern 4b: Named tuple For(((Type1 Name1, Type2 Name2) param) => new Query { ... })
        var namedTupleMatch = Regex.Match(normalized,
            @"ResourceEventDefinition\s*\.\s*For\s*\(\s*\(\((?<domain>[^)]+\s+\w+(?:,\s*[^)]+\s+\w+)+)\)\s+(?<param>\w+)\)\s*=>\s*(?<body>new\s+.+?\})\s*\)",
            RegexOptions.Singleline);
        if (namedTupleMatch.Success)
        {
            return new FactoryDefinition(
                fieldName,
                resourceType,
                "(" + namedTupleMatch.Groups["domain"].Value.Trim() + ")",
                FactoryKind.SelfQuery,
                ExtractQueryType(namedTupleMatch.Groups["body"].Value),
                namedTupleMatch.Groups["param"].Value.Trim(),
                namedTupleMatch.Groups["body"].Value.Trim());
        }

        // Pattern 5: Explicit typed param with mapper For((DomainType param) => Mapper.Method(param))
        var mapperMatch = Regex.Match(normalized,
            @"ResourceEventDefinition\s*\.\s*For\s*\(\s*\((?<domain>[^)]+)\s+(?<param>\w+)\)\s*=>\s*(?<body>[^;]+?)\s*\)\s*;");
        if (mapperMatch.Success && !mapperMatch.Groups["body"].Value.TrimStart().StartsWith("new "))
        {
            return new FactoryDefinition(
                fieldName,
                resourceType,
                mapperMatch.Groups["domain"].Value.Trim(),
                FactoryKind.Mapper,
                null,
                mapperMatch.Groups["param"].Value.Trim(),
                mapperMatch.Groups["body"].Value.Trim());
        }

        // Pattern 6: No mapper — For<TResource>()
        var noMapperMatch = Regex.Match(normalized, @"ResourceEventDefinition\s*\.\s*For<(?<resource>[^>]+)>\s*\(\s*\)");
        if (noMapperMatch.Success)
        {
            return new FactoryDefinition(
                fieldName,
                noMapperMatch.Groups["resource"].Value.Trim(),
                null,
                FactoryKind.NoMapper,
                null,
                null,
                null);
        }

        // Pattern 7: Method group mapper — For<TResource, TDomain>(MapperClass.Method)
        var methodGroupMatch = Regex.Match(normalized,
            @"ResourceEventDefinition\s*\.\s*For<(?<resource>[^,>]+),\s*(?<domain>[^>]+)>\s*\(\s*(?<body>[\w.]+)\s*\)");
        if (methodGroupMatch.Success)
        {
            var body = methodGroupMatch.Groups["body"].Value.Trim();
            // This is a mapper pattern: the method group IS the mapper
            return new FactoryDefinition(
                fieldName,
                methodGroupMatch.Groups["resource"].Value.Trim(),
                methodGroupMatch.Groups["domain"].Value.Trim(),
                FactoryKind.Mapper,
                null,
                "x",
                $"{body}(x)");
        }

        Console.WriteLine($"  WARNING: Could not parse factory statement for {fieldName}");
        Console.WriteLine($"    Normalized: {normalized[..Math.Min(200, normalized.Length)]}");
        return null;
    }

    private static List<EventDefinition> ParseEvents(string classContent)
    {
        var events = new List<EventDefinition>();

        foreach (Match match in EventDefinitionRegex.Matches(classContent))
        {
            var changeType = match.Groups["method"].Value switch
            {
                "Created" => ChangeTypeKind.Created,
                "Updated" => ChangeTypeKind.Updated,
                "Deleted" => ChangeTypeKind.Deleted,
                _ => throw new InvalidOperationException($"Unknown change type: {match.Groups["method"].Value}"),
            };

            events.Add(new EventDefinition(
                match.Groups["name"].Value,
                match.Groups["factory"].Value,
                changeType,
                match.Groups["subId"].Success && match.Groups["subId"].Value.Length > 0
                    ? match.Groups["subId"].Value
                    : null));
        }

        return events;
    }

    private static string? ExtractQueryType(string lambdaBody)
    {
        var match = Regex.Match(lambdaBody, @"new\s+(?<type>\w+)");
        return match.Success ? match.Groups["type"].Value : null;
    }

    private static string ExtractResourceTypeFromField(string statement)
    {
        // Extract from ResourceEventDefinitionFactoryWithMapper<TResource, ...> or similar
        var match = Regex.Match(statement, @"(?:ResourceEventDefinitionFactory(?:WithMapper)?|ResourceEventDefinitionFactory)<(?<resource>[^,>]+)");
        return match.Success ? match.Groups["resource"].Value.Trim() : "UNKNOWN";
    }
}

