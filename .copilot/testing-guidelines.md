# Testing Guidelines and Patterns

## Testing Framework Stack

### Core Testing Libraries
```xml
<!-- Primary testing dependencies -->
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="FluentAssertions" Version="7.2.0" />
<PackageReference Include="NSubstitute" Version="5.3.0" />

<!-- Source generator testing -->
<PackageReference Include="Verify.SourceGenerators" Version="2.5.0" />
<PackageReference Include="Verify.XUnit" Version="28.16.0" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.9.2" />

<!-- Additional testing utilities -->
<PackageReference Include="xunit.combinatorial" Version="1.6.24" />
```

### Global Test Usings
```csharp
// Automatically available in all test files via Directory.Build.props
global using FluentAssertions;
global using Xunit;
global using NSubstitute;
```

## Test File Organization

### Directory Structure
```
Tests/
├── Generators/
│   ├── App/
│   │   ├── AppDataModelSourceGeneratorTests.cs
│   │   └── Snapshots/                    # Verified generated code
│   ├── ApiResources/
│   └── Descriptors/
├── Helpers/
├── Model/
└── Integration/                          # End-to-end tests
```

### Test Class Naming
```csharp
// ✅ Pattern: {FeatureUnderTest}Tests
public class AppDataModelSourceGeneratorTests { }
public class BlueprintAppTests { }
public class GetTemplatesQueryTests { }
public class TemplateApiResourceTests { }
```

## Source Generator Testing Patterns

### Basic Generator Test Structure
```csharp
public class AppDataModelSourceGeneratorTests
{
    [Fact]
    public async Task GeneratesRegistration_ForSingleOperation()
    {
        // Arrange - Define input source code
        const string input = """
            using Blueprint.Model;
            
            [BlueprintAppScan]
            public partial class TestApp : BlueprintApp { }
            
            [HttpGet]
            public partial class TestQuery : IQuery<string> { }
            """;
        
        // Act - Compile with source generator
        var result = TestHelpers.Compile<AppDataModelSourceGenerator>(input);
        
        // Assert - Verify generated code matches snapshot
        await result.VerifySuccessAsync(GetSnapshotsFolder());
    }
    
    private string GetSnapshotsFolder([CallerFilePath] string testFile = "")
    {
        return Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots");
    }
}
```

### Parameterized Generator Tests
```csharp
[Theory]
[InlineData("HttpGet", "GET")]
[InlineData("HttpPost", "POST")]
[InlineData("HttpPut", "PUT")]
[InlineData("HttpDelete", "DELETE")]
public async Task GeneratesCorrectHttpMethod_ForAttribute(string attribute, string expectedMethod)
{
    const string inputTemplate = """
        using Blueprint.Model;
        
        [BlueprintAppScan]
        public partial class TestApp : BlueprintApp { }
        
        [{0}]
        public partial class TestOperation : IQuery<string> { }
        """;
        
    var input = string.Format(inputTemplate, attribute);
    var result = TestHelpers.Compile<AppDataModelSourceGenerator>(input);
    
    await result.VerifySuccessAsync(GetSnapshotsFolder(), 
        testName: $"{nameof(GeneratesCorrectHttpMethod_ForAttribute)}_{attribute}");
}
```

### Complex Scenario Testing
```csharp
[Fact]
public async Task GeneratesLinks_ForMultipleResourceTypes()
{
    const string input = """
        using Blueprint.Model;
        using Blueprint.Model.Attributes;
        
        [BlueprintAppScan]
        public partial class TestApp : BlueprintApp { }
        
        public class UserApiResource : ApiResource
        {
            public string Id { get; set; } = null!;
        }
        
        public class OrderApiResource : ApiResource
        {
            public string Id { get; set; } = null!;
        }
        
        [SelfLink(typeof(UserApiResource), "/users/{id}")]
        [LinkAttribute(typeof(OrderApiResource), "/users/{id}/orders", "orders")]
        [HttpGet]
        public partial class GetUserQuery : IQuery<UserApiResource> { }
        """;
        
    var result = TestHelpers.Compile<AppDataModelSourceGenerator>(input);
    await result.VerifySuccessAsync(GetSnapshotsFolder());
}
```

## Snapshot Testing Best Practices

### Snapshot File Management
```csharp
// ✅ Use descriptive test names for clear snapshot files
[Fact]
public async Task GeneratesBlueprintApp_WithAuthenticatedOperation()
{
    // Snapshot will be saved as:
    // Snapshots/GeneratesBlueprintApp_WithAuthenticatedOperation.verified.txt
}

[Theory]
[InlineData("Simple")]
[InlineData("WithParameters")]
public async Task GeneratesQuery_ForScenario(string scenario)
{
    // Snapshots will be saved as:
    // Snapshots/GeneratesQuery_ForScenario_Simple.verified.txt
    // Snapshots/GeneratesQuery_ForScenario_WithParameters.verified.txt
    
    await result.VerifySuccessAsync(GetSnapshotsFolder(), testName: $"{nameof(GeneratesQuery_ForScenario)}_{scenario}");
}
```

### Snapshot Content Control
```csharp
// ✅ Include marker attributes when relevant to test
await result.VerifySuccessAsync(GetSnapshotsFolder(), includeMarkerAttributes: true);

// ✅ Exclude marker attributes for cleaner snapshots (default)
await result.VerifySuccessAsync(GetSnapshotsFolder(), includeMarkerAttributes: false);
```

### Snapshot Verification Settings
```csharp
// ModuleInitializer.cs - Global Verify settings
[ModuleInitializer]
public static void Initialize()
{
    // Configure Verify to use custom settings
    VerifySourceGenerators.Enable();
    
    // Customize diff tools, ignore whitespace differences, etc.
    DiffRunner.Disabled = true; // For CI environments
}
```

## Unit Testing Patterns

### Testing API Resources
```csharp
public class TemplateApiResourceTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        const string id = "template-123";
        const string name = "Test Template";
        const string content = "<html>Test</html>";
        
        // Act
        var resource = new TemplateApiResource(id, name, content);
        
        // Assert
        resource.Id.Should().Be(id);
        resource.Name.Should().Be(name);
        resource.Content.Should().Be(content);
    }
    
    [Fact]
    public void JsonSerialization_RoundTrip_PreservesData()
    {
        // Arrange
        var original = new TemplateApiResource("id", "name", "content");
        
        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<TemplateApiResource>(json);
        
        // Assert
        deserialized.Should().BeEquivalentTo(original);
    }
}
```

### Testing Operation Logic
```csharp
public class GetTemplatesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsFilteredTemplates_WhenCategoryProvided()
    {
        // Arrange
        var repository = Substitute.For<ITemplateRepository>();
        var handler = new GetTemplatesQueryHandler(repository);
        var query = new GetTemplatesQuery { CategoryFilter = "Personal" };
        
        var expectedTemplates = new[]
        {
            new TemplateApiResource("1", "Template 1", "Content 1"),
            new TemplateApiResource("2", "Template 2", "Content 2")
        };
        
        repository.GetByCategory("Personal").Returns(expectedTemplates);
        
        // Act
        var result = await handler.HandleAsync(query);
        
        // Assert
        result.Should().BeEquivalentTo(expectedTemplates);
    }
    
    [Fact]
    public async Task Handle_ThrowsNotFoundException_WhenCategoryNotFound()
    {
        // Arrange
        var repository = Substitute.For<ITemplateRepository>();
        var handler = new GetTemplatesQueryHandler(repository);
        var query = new GetTemplatesQuery { CategoryFilter = "NonExistent" };
        
        repository.GetByCategory("NonExistent").Returns((TemplateApiResource[])null);
        
        // Act & Assert
        var act = () => handler.HandleAsync(query);
        await act.Should().ThrowAsync<NotFoundException>()
                 .WithMessage("Category 'NonExistent' not found");
    }
}
```

### Testing BlueprintApp Registration
```csharp
public class SampleBlueprintAppTests
{
    [Fact]
    public void Constructor_RegistersAllOperations()
    {
        // Act
        var app = new SampleBlueprintApp();
        
        // Assert
        var operations = app.Operations.ToList();
        operations.Should().HaveCount(2);
        operations.Should().Contain(o => o.OperationType == typeof(GetTemplatesQuery));
        operations.Should().Contain(o => o.OperationType == typeof(CreateTemplateCommand));
    }
    
    [Fact]
    public void GetLinksForResource_ReturnsCorrectLinks_ForTemplateResource()
    {
        // Arrange
        var app = new SampleBlueprintApp();
        
        // Act
        var links = app.GetLinksForResource(typeof(TemplateApiResource));
        
        // Assert
        links.Should().HaveCount(1);
        links.Should().Contain(l => l.Rel == "self" && l.UrlFormat == "/templates/{id}");
    }
}
```

## Integration Testing Patterns

### End-to-End API Tests
```csharp
public class TemplateApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public TemplateApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
    
    [Fact]
    public async Task GetTemplates_ReturnsSuccessAndCorrectContentType()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Act
        var response = await client.GetAsync("/api/templates");
        
        // Assert
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.ToString()
               .Should().Be("application/json; charset=utf-8");
    }
    
    [Fact]
    public async Task CreateTemplate_WithValidData_ReturnsCreated()
    {
        // Arrange
        var client = _factory.CreateClient();
        var template = new { Name = "Test", Content = "<html>Test</html>" };
        var json = JsonSerializer.Serialize(template);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        // Act
        var response = await client.PostAsync("/api/templates", content);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

## Test Helpers and Utilities

### Custom Test Helpers
```csharp
public static class TestHelpers
{
    public static GenerationResult Compile<T>(string source, bool includeBaseReferences = true)
        where T : IIncrementalGenerator, new()
    {
        // Implementation in src/Blueprint.SourceGenerator.Tests/TestHelpers.cs
        // Creates compilation with source generator and returns results
    }
    
    public static string GetSnapshotsFolder([CallerFilePath] string testFile = "")
    {
        return Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots");
    }
}
```

### Custom Assertions
```csharp
public static class CustomAssertions
{
    public static void ShouldHaveOperation<T>(this BlueprintApp app) where T : IOperation
    {
        var operation = app.Operations.SingleOrDefault(o => o.OperationType == typeof(T));
        operation.Should().NotBeNull($"Operation {typeof(T).Name} should be registered");
    }
    
    public static void ShouldHaveLink(this BlueprintApp app, Type resourceType, string rel)
    {
        var link = app.GetLinkFor(resourceType, rel);
        link.Should().NotBeNull($"Link with rel '{rel}' should exist for {resourceType.Name}");
    }
}
```

## Test Execution and CI

### Running Tests Locally
```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test project
dotnet test src/Blueprint.SourceGenerator.Tests/

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Test Categories and Filtering
```csharp
[Fact]
[Trait("Category", "Unit")]
public void UnitTest() { }

[Fact]
[Trait("Category", "Integration")]
public void IntegrationTest() { }

// Run only unit tests:
// dotnet test --filter "Category=Unit"
```

### Performance Testing
```csharp
[Fact]
public void SourceGenerator_PerformanceTest()
{
    var stopwatch = Stopwatch.StartNew();
    
    // Act
    var result = TestHelpers.Compile<AppDataModelSourceGenerator>(largeInput);
    
    // Assert
    stopwatch.Stop();
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, 
        "Source generation should complete within 1 second");
}
```