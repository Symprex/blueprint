using System.Text.RegularExpressions;
using ResourceEventMigration;

/// <summary>
/// Main orchestrator for the ResourceEvent migration tool.
/// Scans a target solution directory, parses *Events.cs files, generates replacement records,
/// and transforms command interfaces and handler return statements.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ResourceEventMigration <target-solution-root> [--dry-run]");
            Console.WriteLine();
            Console.WriteLine("  target-solution-root  Path to the S365 repository root");
            Console.WriteLine("  --dry-run             Show what would be changed without modifying files");
            return 1;
        }

        var targetRoot = args[0];
        var dryRun = args.Contains("--dry-run");

        if (!Directory.Exists(targetRoot))
        {
            Console.Error.WriteLine($"ERROR: Target directory not found: {targetRoot}");
            return 1;
        }

        Console.WriteLine($"ResourceEvent Migration Tool");
        Console.WriteLine($"Target: {targetRoot}");
        Console.WriteLine($"Mode: {(dryRun ? "DRY RUN" : "APPLY")}");
        Console.WriteLine(new string('=', 60));

        // Phase 1: Parse all *Events.cs files
        Console.WriteLine("\n[Phase 1] Parsing *Events.cs files...");
        var searchRoots = new[] { "src\\Apps", "src\\Contract" }
            .Select(d => Path.Combine(targetRoot, d))
            .Where(Directory.Exists)
            .ToArray();

        var eventsFiles = searchRoots
            .SelectMany(d => Directory.EnumerateFiles(d, "*Events.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .Where(f => File.ReadAllText(f).Contains("ResourceEventDefinition"))
            .ToList();

        Console.WriteLine($"  Found {eventsFiles.Count} Events files with ResourceEventDefinition");

        var allEventsClasses = new List<EventsClass>();
        foreach (var file in eventsFiles)
        {
            try
            {
                var parsed = EventsFileParser.Parse(file);
                allEventsClasses.AddRange(parsed);
                foreach (var ec in parsed)
                {
                    Console.WriteLine($"  ✓ {ec.ClassName}: {ec.Factories.Count} factories, {ec.Events.Count} events");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ ERROR parsing {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // Phase 2: Build event mapping table
        Console.WriteLine("\n[Phase 2] Building event mapping table...");
        var allMappings = new Dictionary<string, EventMapping>(StringComparer.Ordinal);
        foreach (var eventsClass in allEventsClasses)
        {
            var mappings = EventsFileGenerator.GetEventMappings(eventsClass);
            foreach (var mapping in mappings)
            {
                allMappings[mapping.OldReference] = mapping;
                Console.WriteLine($"  {mapping.OldReference} → {mapping.NewClassName}.{mapping.RecordName} ({mapping.Factory.Kind})");
            }
        }

        Console.WriteLine($"\n  Total mappings: {allMappings.Count}");

        // Phase 3: Generate replacement [ResourceEventFamily] record files
        Console.WriteLine("\n[Phase 3] Generating [ResourceEventFamily] record files...");
        var generatedFiles = new List<(string Path, string Content)>();
        var migratedEventsFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var eventsFile in eventsFiles)
        {
            var parsed = EventsFileParser.Parse(eventsFile);
            foreach (var eventsClass in parsed)
            {
                if (eventsClass.Events.Count == 0) continue;

                var content = EventsFileGenerator.Generate(eventsClass);
                // New file goes next to the old one with "ResourceEvents" suffix
                var dir = Path.GetDirectoryName(eventsFile)!;
                var newFileName = eventsClass.ClassName.Replace("Events", "ResourceEvents") + ".cs";
                var newFilePath = Path.Combine(dir, newFileName);

                generatedFiles.Add((newFilePath, content));
                migratedEventsFiles.Add(eventsFile);
                Console.WriteLine($"  → {Path.GetRelativePath(targetRoot, newFilePath)}");
            }
        }

        // Phase 4: Transform command interface files
        Console.WriteLine("\n[Phase 4] Transforming command interfaces...");
        var searchDirs = new[] { "src\\Apps", "src\\Contract" }
            .Select(d => Path.Combine(targetRoot, d))
            .Where(Directory.Exists)
            .ToArray();

        var commandFiles = searchDirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .Where(f =>
            {
                try { return File.ReadAllText(f).Contains("ICommand<ResourceEvent<"); }
                catch { return false; }
            })
            .ToList();

        Console.WriteLine($"  Found {commandFiles.Count} files with ICommand<ResourceEvent<T>>");

        var transformedCommands = new List<(string Path, string Content)>();
        foreach (var file in commandFiles)
        {
            var content = File.ReadAllText(file);
            var transformed = HandlerTransformer.TransformCommandInterface(content, allMappings);
            transformed = HandlerTransformer.AddRequiredUsings(transformed);

            if (transformed != content)
            {
                transformedCommands.Add((file, transformed));
                Console.WriteLine($"  ✓ {Path.GetRelativePath(targetRoot, file)}");
            }
        }

        // Phase 5: Transform handler return statements
        Console.WriteLine("\n[Phase 5] Transforming handler return statements...");
        var handlerFiles = searchDirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*Handler*.cs", SearchOption.AllDirectories))
            .Concat(searchDirs.SelectMany(d => Directory.EnumerateFiles(d, "*CommandHandler*.cs", SearchOption.AllDirectories)))
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .Distinct()
            .Where(f =>
            {
                try
                {
                    var content = File.ReadAllText(f);
                    // Match .New() calls (possibly multiline) or Task<ResourceEvent<T>> return types
                    return Regex.IsMatch(content, @"\w+Events?\.\w+\s*\.New\(", RegexOptions.Singleline) ||
                           content.Contains("Task<ResourceEvent<");
                }
                catch { return false; }
            })
            .ToList();

        // Also check files that have ResourceEvent return types but might not be named *Handler*
        var additionalFiles = searchDirs
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\") && !handlerFiles.Contains(f))
            .Where(f =>
            {
                try
                {
                    var content = File.ReadAllText(f);
                    return (Regex.IsMatch(content, @"\w+Events?\.\w+\s*\.New\(", RegexOptions.Singleline) ||
                            content.Contains("Task<ResourceEvent<") ||
                            content.Contains("ResourceEvent<")) &&
                           !content.Contains("[ResourceEventFamily");
                }
                catch { return false; }
            })
            .ToList();

        handlerFiles.AddRange(additionalFiles);
        Console.WriteLine($"  Found {handlerFiles.Count} files with .New() calls or ResourceEvent return types");

        var transformedHandlers = new List<(string Path, string Content)>();
        foreach (var file in handlerFiles)
        {
            var content = File.ReadAllText(file);
            var transformed = HandlerTransformer.TransformHandlerReturns(content, allMappings);
            transformed = HandlerTransformer.TransformCommandInterface(transformed, allMappings);
            transformed = HandlerTransformer.TransformDataPropertyAccess(transformed);
            transformed = HandlerTransformer.AddRequiredUsings(transformed);
            transformed = HandlerTransformer.CleanupUsings(transformed);

            if (transformed != content)
            {
                transformedHandlers.Add((file, transformed));
                Console.WriteLine($"  ✓ {Path.GetRelativePath(targetRoot, file)}");
            }
        }

        // Phase 6: Apply changes (or report in dry-run)
        Console.WriteLine($"\n[Phase 6] {(dryRun ? "Summary (dry run)" : "Applying changes")}...");
        Console.WriteLine($"  New files to create: {generatedFiles.Count}");
        Console.WriteLine($"  Command files to transform: {transformedCommands.Count}");
        Console.WriteLine($"  Handler files to transform: {transformedHandlers.Count}");

        if (!dryRun)
        {
            // Write new record files
            foreach (var (path, content) in generatedFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content);
            }

            // Transform command interfaces
            foreach (var (path, content) in transformedCommands)
            {
                await File.WriteAllTextAsync(path, content);
            }

            // Transform handlers
            foreach (var (path, content) in transformedHandlers)
            {
                await File.WriteAllTextAsync(path, content);
            }

            // Remove old Events.cs files (only those that were successfully migrated)
            Console.WriteLine("\n  Removing old *Events.cs files...");
            foreach (var file in eventsFiles.Where(f => migratedEventsFiles.Contains(f)))
            {
                File.Delete(file);
                Console.WriteLine($"  ✗ Deleted: {Path.GetRelativePath(targetRoot, file)}");
            }

            Console.WriteLine("\n  ✓ All changes applied successfully!");
        }

        Console.WriteLine($"\n{new string('=', 60)}");
        Console.WriteLine("Migration complete.");
        return 0;
    }
}
