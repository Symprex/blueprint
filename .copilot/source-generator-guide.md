# Source Generator Development Guide

## Roslyn Source Generator Architecture

### Generator Types and Requirements

**IIncrementalGenerator** (Recommended)
```csharp
[Generator(LanguageNames.CSharp)]
public sealed partial class AppDataModelSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Incremental pipeline setup
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Blueprint.Model.BlueprintAppScanAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ParseDataModelWithScanAttributes(ctx))
            .WithTrackingName("InitialExtraction"); // For debugging
    }
}
```

**Key Requirements:**
- Target `netstandard2.0` for Roslyn compatibility
- Use `IsRoslynComponent=true` in project file
- Enable `EnforceExtendedAnalyzerRules=true`
- Package as analyzer: `<None Include="$(OutputPath)\*.dll" Pack="true" PackagePath="analyzers/dotnet/cs" />`

### Generator Project Configuration
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Core Roslyn dependencies -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.9.2" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
    
    <!-- Code generation helpers -->
    <PackageReference Include="AvantiPoint.CodeGenHelpers" Version="2.0.30-pre" PrivateAssets="all" GeneratePathProperty="true"/>
  </ItemGroup>
</Project>
```

## Blueprint Generator Pipeline

### Three-Stage Generation Process

**Stage 1: Initial Extraction**
```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    // Find classes with [BlueprintAppScan] attribute
    var classProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
        "Blueprint.Model.BlueprintAppScanAttribute",
        predicate: static (syntaxNode, _) => syntaxNode is ClassDeclarationSyntax,
        transform: static (context, _) => ParseDataModelWithScanAttributes(context)!)
        .Where(method => method != null)
        .WithTrackingName(Stages.InitialExtraction);
}
```

**Stage 2: Combine with Compilation**
```csharp
var combinedProvider = classProvider.Combine(context.CompilationProvider)
    .WithComparer(CombinedProviderComparer<Result<ParseOnlyDataModel>>.Instance)
    .WithTrackingName(Stages.CombineWithCompilation);
```

**Stage 3: Find Operations to Register**
```csharp
var generationModelProvider = combinedProvider
    .Select((scanModelContext, _) => FindOperations(scanModelContext))
    .WithTrackingName(Stages.FindServicesToRegister);

context.RegisterSourceOutput(generationModelProvider, GenerateSourceOutput);
```

### Parsing and Analysis

**Attribute Detection**
```csharp
private static Result<ParseOnlyDataModel>? ParseDataModelWithScanAttributes(
    GeneratorAttributeSyntaxContext context)
{
    var classSymbol = (INamedTypeSymbol)context.TargetSymbol;
    var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
    
    // Validate class is partial
    if (!classSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
    {
        return Result<ParseOnlyDataModel>.WithDiagnostic(
            _missingPartialModifierRule,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name);
    }
    
    // Parse scan attributes
    var scanAttributes = ParseScanAttributes(context.Attributes);
    
    return Result<ParseOnlyDataModel>.WithValue(new ParseOnlyDataModel(
        classSymbol, classSyntax, scanAttributes));
}
```

**Operation Discovery**
```csharp
private static Result<BlueprintAppDataModelCompilationModel> FindOperations(
    (Result<ParseOnlyDataModel> ParseModel, Compilation Compilation) input)
{
    var parseModel = input.ParseModel.Value!;
    var compilation = input.Compilation;
    
    var operations = new List<INamedTypeSymbol>();
    
    // Find all types implementing IOperation in specified assemblies
    foreach (var scanAttribute in parseModel.ScanAttributes)
    {
        var assembly = scanAttribute.FromAssembly ?? compilation.Assembly;
        var operationTypes = FindOperationTypesInAssembly(assembly, compilation);
        operations.AddRange(operationTypes);
    }
    
    return Result<BlueprintAppDataModelCompilationModel>.WithValue(
        new BlueprintAppDataModelCompilationModel(parseModel.Class, operations));
}
```

## Code Generation Patterns

### Using CodeGenHelpers Library
```csharp
private static string GenerateRegistrations(BlueprintAppDataModelCompilationModel compilationModel)
{
    var codeBuilder = CodeBuilder.Create(compilationModel.Class.Namespace!);
    var classBuilder = codeBuilder.AddClass(compilationModel.Class.Name)
        .WithAccessModifier(Accessibility.Public);
        
    classBuilder.AddConstructor()
        .WithBody(c =>
        {
            foreach (var operation in compilationModel.Operations)
            {
                c.AppendLine($"this.Register({operation.Name}.Configure());");
            }
        });
        
    return codeBuilder.Build();
}
```

### Manual Code Generation
```csharp
private static string GenerateOperationDescriptor(INamedTypeSymbol operationType)
{
    var sb = new StringBuilder();
    sb.AppendLine("using Blueprint.Model;");
    sb.AppendLine();
    sb.AppendLine($"namespace {operationType.ContainingNamespace};");
    sb.AppendLine();
    sb.AppendLine($"partial class {operationType.Name}");
    sb.AppendLine("{");
    sb.AppendLine("    public static ApiOperationDescriptor Configure()");
    sb.AppendLine("    {");
    sb.AppendLine($"        return new ApiOperationDescriptor(typeof({operationType.Name}))");
    sb.AppendLine("        {");
    sb.AppendLine($"            Name = \"{operationType.Name}\",");
    
    // Generate URLs, links, etc.
    GenerateUrls(sb, operationType);
    GenerateLinks(sb, operationType);
    
    sb.AppendLine("        };");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    
    return sb.ToString();
}
```

## Diagnostic Reporting

### Diagnostic Descriptors
```csharp
private static readonly DiagnosticDescriptor _missingPartialModifierRule = new(
    id: "BP002",
    title: "Class must be partial",
    messageFormat: "The class '{0}' must be partial to generate data model registrations",
    category: "AppDataModelSourceGenerator",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

private static readonly DiagnosticDescriptor _noMatchingTypesFound = new(
    "BP003",
    "No matching types found",
    "There are no types matching attribute's search criteria",
    "Usage",
    DiagnosticSeverity.Error,
    true);
```

### Reporting Diagnostics
```csharp
public record Result<T>
{
    public T? Value { get; init; }
    public ImmutableArray<Diagnostic> Diagnostics { get; init; } = ImmutableArray<Diagnostic>.Empty;
    
    public static Result<T> WithDiagnostic(DiagnosticDescriptor descriptor, Location location, params object[] args)
    {
        var diagnostic = Diagnostic.Create(descriptor, location, args);
        return new Result<T> { Diagnostics = ImmutableArray.Create(diagnostic) };
    }
    
    public void ReportDiagnostics(SourceProductionContext context)
    {
        foreach (var diagnostic in Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }
}
```

## Module System Architecture

### Module Interface
```csharp
public interface IBlueprintSourceGeneratorModule
{
    // Register marker attributes that this module needs
    void RegisterMarkerAttributes(IncrementalGeneratorPostInitializationContext initializationContext);
    
    // Configure operation model during analysis phase
    void ConfigureModel(OperationConfigurationModel registration);
    
    // Generate code for the descriptor
    void ConfigureDescriptor(ICodeWriter codeWriter, OperationToRegisterModel registration);
}
```

### Example Module Implementation
```csharp
public class LinkGenerationModule : IBlueprintSourceGeneratorModule
{
    public void RegisterMarkerAttributes(IncrementalGeneratorPostInitializationContext initializationContext)
    {
        // Generate SelfLinkAttribute and other marker attributes
        var selfLinkAttributeSource = """
            namespace Blueprint.Model.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public class SelfLinkAttribute : LinkAttribute
                {
                    public SelfLinkAttribute(System.Type resourcetype, string url) : base(resourcetype, url, "self") { }
                }
            }
            """;
            
        initializationContext.AddSource("SelfLinkAttribute_MarkerAttribute.g.cs", selfLinkAttributeSource);
    }
    
    public void ConfigureModel(OperationConfigurationModel registration)
    {
        // Analyze attributes and store metadata
        var linkAttributes = registration.OperationType
            .GetAttributes()
            .Where(a => a.AttributeClass?.Name == "LinkAttribute")
            .ToArray();
            
        registration.SetMetadata("Links", linkAttributes);
    }
    
    public void ConfigureDescriptor(ICodeWriter codeWriter, OperationToRegisterModel registration)
    {
        if (registration.GetMetadata<AttributeData[]>("Links") is { Length: > 0 } links)
        {
            codeWriter.AppendLine("Links =");
            codeWriter.AppendLine("{");
            
            foreach (var link in links)
            {
                GenerateLinkCode(codeWriter, link);
            }
            
            codeWriter.AppendLine("},");
        }
    }
}
```

## Performance Optimization

### Incremental Generation Best Practices
```csharp
// ✅ Use specific predicates to reduce work
var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
    "Blueprint.Model.BlueprintAppScanAttribute",
    predicate: static (syntaxNode, _) => 
        syntaxNode is ClassDeclarationSyntax { Modifiers: var modifiers } &&
        modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)), // Early filter
    transform: ParseClass);

// ✅ Use caching for expensive operations
private static readonly ConcurrentDictionary<string, ComputedValue> _cache = new();

// ✅ Use WithTrackingName for debugging
.WithTrackingName("OperationDiscovery");

// ✅ Implement proper equality comparers
public class OperationComparer : IEqualityComparer<INamedTypeSymbol>
{
    public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y)
        => SymbolEqualityComparer.Default.Equals(x, y);
    
    public int GetHashCode(INamedTypeSymbol obj)
        => SymbolEqualityComparer.Default.GetHashCode(obj);
}
```

### Memory Management
```csharp
// ✅ Use static readonly for collections
private static readonly ImmutableArray<string> _httpMethods = 
    ImmutableArray.Create("GET", "POST", "PUT", "DELETE", "PATCH");

// ✅ Pool StringBuilder instances
private static readonly ObjectPool<StringBuilder> _stringBuilderPool = 
    new DefaultObjectPoolProvider().CreateStringBuilderPool();

// ✅ Avoid allocations in hot paths
public static bool IsOperationType(INamedTypeSymbol type)
{
    // Check interfaces without creating new collections
    return type.AllInterfaces.Any(i => 
        i.Name == "IOperation" && 
        i.ContainingNamespace?.Name == "Blueprint.Model");
}
```

## Debugging Source Generators

### Enable Generator Debugging
```xml
<!-- In consuming project -->
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

### Debug Output and Tracking
```csharp
// Use tracking names to identify bottlenecks
.WithTrackingName("InitialExtraction")     // Stage 1
.WithTrackingName("CombineWithCompilation") // Stage 2  
.WithTrackingName("FindServicesToRegister") // Stage 3

// Add debug output during development
#if DEBUG
private static void DebugLog(string message)
{
    System.Diagnostics.Debug.WriteLine($"[Blueprint Generator] {message}");
}
#endif
```

### Testing Generator Performance
```csharp
[Fact]
public void Generator_CompletesWithinTimeLimit()
{
    var stopwatch = Stopwatch.StartNew();
    
    var result = TestHelpers.Compile<AppDataModelSourceGenerator>(largeInput);
    
    stopwatch.Stop();
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, 
        "Generator should complete within 5 seconds for large inputs");
}
```

## Common Pitfalls and Solutions

### Avoiding Common Issues
```csharp
// ❌ Don't use mutable state in generators
private static List<string> _operations = new(); // WRONG - shared state

// ✅ Use immutable collections and local state
private static ImmutableArray<string> ProcessOperations(IEnumerable<string> input)
{
    return input.ToImmutableArray();
}

// ❌ Don't reference runtime assemblies from generator
// Generator runs in separate process and can't access runtime types

// ✅ Use symbol analysis instead
private static bool IsApiResource(INamedTypeSymbol type)
{
    return type.BaseType?.Name == "ApiResource";
}

// ❌ Don't generate duplicate source files
context.AddSource($"Generated.cs", source); // Could conflict

// ✅ Use unique, descriptive file names
context.AddSource($"{className}_DataModelRegistration.g.cs", source);
```

### Error Handling in Generators
```csharp
// ✅ Graceful error handling with diagnostics
try
{
    var result = ProcessOperation(operationType);
    return Result<T>.WithValue(result);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return Result<T>.WithDiagnostic(
        _processingErrorRule,
        operationType.Locations.FirstOrDefault(),
        operationType.Name,
        ex.Message);
}
```