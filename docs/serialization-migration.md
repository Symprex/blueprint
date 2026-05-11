# Serialization Guide: JsonSerializerContext Setup for Blueprint

This guide covers setting up source-generated JSON serialization for Blueprint
source-gen consumers. Blueprint requires an app-owned `JsonSerializerContext`
declared via `[BlueprintAppSerializerContext]`.

> **Note for Signature365:** S365 already uses System.Text.Json (STJ) for all
> API request/response serialization. Newtonsoft.Json is only used for Marten
> document storage (via `JsonNetSerializer`) and is unaffected by this migration.
> The main task is adding a `JsonSerializerContext` with `[JsonSerializable]`
> entries for all HTTP body types.

## Why the JsonSerializerContext is Required

Blueprint source-gen requires an app-owned `JsonSerializerContext` declared via
`[BlueprintAppSerializerContext]`. This enables:

- **Compile-time validation** — `BP007` fails if the attribute is missing, `BP006`
  fails if body types are not covered by `[JsonSerializable]` entries
- **AOT compatibility** — No reflection-based serialization at runtime
- **Performance** — Source-generated serializers are faster than reflection-based ones
- **Determinism** — The serialization shape is known at build time

## Setup Checklist

### 1. Inventory HTTP Body Types

```powershell
# Find all files referencing Newtonsoft
rg "Newtonsoft" --type cs -l

# Find JsonProperty attributes
rg "\[JsonProperty" --type cs -l

# Find custom converters
rg "JsonConverter" --type cs -l

# Find JObject/JToken/JArray usage
rg "JObject|JToken|JArray|JValue" --type cs -l

# Find dynamic JSON usage
rg "JsonConvert\.(Serialize|Deserialize)" --type cs -l
```

### 2. Inventory HTTP Body Types

Every type that appears in an HTTP request body or response body needs a
`[JsonSerializable(typeof(T))]` entry in the app's `JsonSerializerContext`.

```powershell
# Find all IQuery<T> and ICommand<T> response types
rg "IQuery<|ICommand<" --type cs

# Find all ApiResource subclasses
rg ": ApiResource" --type cs

# Find all list/paged resource types
rg "ListApiResource<|PagedApiResource<|ContinuationPagedApiResource<" --type cs
```

### 3. Check NodaTime Serialization

S365 uses NodaTime extensively with `NodaTime.Serialization.JsonNet`. The STJ
equivalent is `NodaTime.Serialization.SystemTextJson`.

```xml
<!-- Remove -->
<PackageReference Include="NodaTime.Serialization.JsonNet" />

<!-- Add -->
<PackageReference Include="NodaTime.Serialization.SystemTextJson" />
```

---

## Attribute Migration Reference

| Newtonsoft.Json | System.Text.Json | Notes |
|----------------|-------------------|-------|
| `[JsonProperty("name")]` | `[JsonPropertyName("name")]` | Property name override |
| `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]` | `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` | Null handling per-property |
| `[JsonIgnore]` | `[JsonIgnore]` | Same attribute name, different namespace |
| `[JsonConverter(typeof(X))]` | `[JsonConverter(typeof(X))]` | Same pattern, different base class |
| `[JsonObject(MemberSerialization.OptIn)]` | No equivalent | STJ is opt-out by default for public properties |
| `[JsonRequired]` | `required` keyword or `[JsonRequired]` (.NET 7+) | Language-level preferred |
| `[JsonExtensionData]` | `[JsonExtensionData]` | Same attribute name |

## Converter Migration Reference

| Newtonsoft Pattern | STJ Pattern |
|-------------------|-------------|
| `JsonConverter` (base) | `JsonConverter<T>` (generic) |
| `WriteJson(JsonWriter, ...)` | `Write(Utf8JsonWriter, T, ...)` |
| `ReadJson(JsonReader, ...)` | `Read(ref Utf8JsonReader, Type, ...)` |
| `CanConvert(Type)` | Type parameter on `JsonConverter<T>` |
| `StringEnumConverter` | `JsonStringEnumConverter` |
| `IsoDateTimeConverter` | Built-in ISO 8601 (default) |

### Example Converter Migration

```csharp
// Before (Newtonsoft)
public class ApiIdConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteValue(((ApiId)value).Value);
    }

    public override object ReadJson(JsonReader reader, Type objectType,
        object existingValue, JsonSerializer serializer)
    {
        return new ApiId((string)reader.Value);
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(ApiId);
}

// After (System.Text.Json)
public class ApiIdConverter : JsonConverter<ApiId>
{
    public override ApiId Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return new ApiId(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, ApiId value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
```

## JObject/JToken Migration

| Newtonsoft | System.Text.Json | Notes |
|-----------|-------------------|-------|
| `JObject` | `JsonObject` (mutable) or `JsonElement` (read-only) | `JsonNode` API for mutable, `JsonElement` for read-only |
| `JToken` | `JsonNode` or `JsonElement` | |
| `JArray` | `JsonArray` or `JsonElement` | |
| `JObject.Parse(json)` | `JsonNode.Parse(json)` or `JsonDocument.Parse(json)` | |
| `jobj["key"]` | `node["key"]` or `element.GetProperty("key")` | |
| `jobj.ToString()` | `node.ToJsonString()` or `JsonSerializer.Serialize(element)` | |
| `jobj.ToObject<T>()` | `node.Deserialize<T>()` or `JsonSerializer.Deserialize<T>(element)` | |

### Example Migration

```csharp
// Before
var jobj = JObject.Parse(json);
var name = jobj["name"]?.ToString();
var items = jobj["items"]?.ToObject<List<string>>();

// After
var node = JsonNode.Parse(json);
var name = node?["name"]?.GetValue<string>();
var items = node?["items"]?.Deserialize<List<string>>();
```

## JsonSerializerContext Setup

### Step 1: Create the Context

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TemplateApiResource))]
[JsonSerializable(typeof(ListApiResource<TemplateApiResource>))]
[JsonSerializable(typeof(PagedApiResource<TemplateApiResource>))]
[JsonSerializable(typeof(UserApiResource))]
[JsonSerializable(typeof(ListApiResource<UserApiResource>))]
// ... every HTTP body type
public partial class AppJsonSerializerContext : JsonSerializerContext { }
```

### Step 2: Declare on BlueprintApp

```csharp
[BlueprintAppScan(FromAssemblyOf = typeof(IPortalApi))]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(AppJsonSerializerContext))]
public partial class PortalBlueprintApp : BlueprintApp { }
```

### Step 3: Configure NodaTime (if applicable)

```csharp
services.AddBlueprintApi<PortalBlueprintApp, PortalBlueprintAppExecutor>(blueprint =>
{
    blueprint.ConfigureHttp(http =>
    {
        // NodaTime converters are registered via the serializer context's options
    });
});
```

For NodaTime with source generation, you may need to register converters at the
`JsonSerializerOptions` level since NodaTime converters are not source-generated:

```csharp
// In startup, configure the shared options
services.Configure<JsonSerializerOptions>(options =>
{
    options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
});
```

**Note:** NodaTime converters use reflection-based serialization. This works fine
in non-AOT scenarios but means NodaTime properties won't benefit from source-gen
speed. For AOT, custom source-generated converters would be needed.

## Common Pitfalls

### 1. Missing `[JsonSerializable]` Entries

**Symptom:** `BP006` build error listing missing types.

**Fix:** Add `[JsonSerializable(typeof(T))]` for every type in the error list.
Use the build error output as your checklist.

### 2. Enum Serialization Differences

```csharp
// Newtonsoft default: integers
// STJ default: integers
// To get string names:

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
// Plus add converter:
[JsonSerializable(typeof(MyEnum))]
public partial class AppJsonSerializerContext : JsonSerializerContext { }

// On the options or property:
[JsonConverter(typeof(JsonStringEnumConverter))]
public MyEnum Status { get; set; }
```

### 3. Null Handling Differences

Newtonsoft defaults to including nulls. STJ also defaults to including nulls,
but the `DefaultIgnoreCondition` in `[JsonSourceGenerationOptions]` applies globally.

If different properties need different null handling, use per-property attributes:
```csharp
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? OptionalField { get; set; }
```

### 4. Default Value Handling

Newtonsoft's `DefaultValueHandling.Ignore` has no direct STJ equivalent for
value types. Use `JsonIgnoreCondition.WhenWritingDefault` for similar behavior:

```csharp
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
public int Count { get; set; }  // Omitted when 0
```

### 5. Polymorphic Serialization

Newtonsoft supports `TypeNameHandling` for polymorphism. STJ uses `[JsonDerivedType]`:

```csharp
[JsonDerivedType(typeof(DerivedA), "typeA")]
[JsonDerivedType(typeof(DerivedB), "typeB")]
public class BaseType { }
```

### 6. Circular Reference Handling

Newtonsoft has `ReferenceLoopHandling.Ignore`. STJ requires explicit configuration:

```csharp
[JsonSourceGenerationOptions(
    ReferenceHandler = typeof(JsonReferenceHandler))] // or ReferenceHandler.Preserve
```

For `ApiResource` types with `$links`, circular references are unlikely since
links are URL strings, not object references.

## Verification Strategy

After migration, verify serialization compatibility:

```csharp
[Theory]
[MemberData(nameof(GetAllBodyTypes))]
public void Serialization_RoundTrip_Matches_Legacy(Type bodyType)
{
    var instance = CreateSampleInstance(bodyType);

    var newtonsoftJson = JsonConvert.SerializeObject(instance, NewtonsoftSettings);
    var stjJson = JsonSerializer.Serialize(instance, AppJsonSerializerContext.Default.Options);

    // Compare JSON shapes (property names, structure)
    var newtonsoftDoc = JsonDocument.Parse(newtonsoftJson);
    var stjDoc = JsonDocument.Parse(stjJson);

    AssertJsonEquivalent(newtonsoftDoc, stjDoc);
}
```

For each service, create this test BEFORE migration and run it AFTER migration to
catch any serialization shape differences that would break clients.
