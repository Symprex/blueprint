using System.Collections.Immutable;
using System.ComponentModel;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Renders stable OpenAPI-style operation contracts by binding operation identity and deterministic URL metadata onto the
/// already-rendered response objects. This stays intentionally narrower than path/document assembly so later slices can
/// group operations by URL without rediscovering descriptor or response facts.
/// </summary>
/// <param name="ResponseObjects">The previously rendered response-object context that owns operation ordering.</param>
/// <param name="Operations">The rendered OpenAPI-style operations for later path/document assembly.</param>
public sealed record OpenApiOperationRenderingContext(
    OpenApiResponseObjectRenderingContext ResponseObjects,
    ImmutableArray<OpenApiOperationModel> Operations)
{
    /// <summary>
    /// Creates deterministic OpenAPI-style operation models from the supplied response-object rendering context.
    /// </summary>
    /// <param name="responseObjects">The response-object rendering context to compose with operation identity and URLs.</param>
    /// <returns>The rendered OpenAPI operation context.</returns>
    public static OpenApiOperationRenderingContext Create(OpenApiResponseObjectRenderingContext responseObjects)
    {
        ArgumentNullException.ThrowIfNull(responseObjects);

        var operations = responseObjects.Operations
            .Select(static operation => CreateOperation(operation))
            .ToImmutableArray();

        return new OpenApiOperationRenderingContext(responseObjects, operations);
    }

    private static OpenApiOperationModel CreateOperation(OpenApiResponseObjectOperationContext operation)
    {
        var httpMethods = operation.Operation.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var httpMethodMetadata)
            ? httpMethodMetadata.HttpMethods
                .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;
        var urlFormats = operation.Operation.Urls
            .Select(static url => url.UrlFormat)
            .OrderBy(static urlFormat => urlFormat, StringComparer.Ordinal)
            .ToImmutableArray();
        var tags = operation.Operation.TryGetFeatureData<OperationGroupMetadataFeatureData>(out var operationGroupMetadata)
            && string.IsNullOrWhiteSpace(operationGroupMetadata.GroupName) == false
            ? ImmutableArray.Create(operationGroupMetadata.GroupName)
            : ImmutableArray<string>.Empty;

        var (parameters, requestBody) = ProjectInputMetadata(operation.Operation);

        // Extract [Description] from the operation type for the operation-level summary
        var operationDescription = operation.Operation.OperationType
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;

        var isDeprecated = operation.Operation.OperationType.IsDefined(typeof(ObsoleteAttribute), inherit: false);

        var requiresAuthentication = operation.Operation.TryGetFeatureData<OperationAuthenticationFeatureData>(out var authData)
            && authData.Requirement == AuthenticationRequirement.Authenticated;

        return new OpenApiOperationModel(
            operation.Operation.Name,
            operation.Operation.OperationType.FullName ?? operation.Operation.OperationType.Name,
            httpMethods,
            urlFormats,
            tags,
            operation.Responses,
            parameters,
            requestBody)
        {
            Summary = operationDescription,
            Deprecated = isDeprecated,
            RequiresAuthentication = requiresAuthentication,
        };
    }

    private static (ImmutableArray<OpenApiParameterModel> Parameters, OpenApiRequestBodyModel? RequestBody)
        ProjectInputMetadata(ApiOperationDescriptor descriptor)
    {
        if (!descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData))
        {
            return (ImmutableArray<OpenApiParameterModel>.Empty, null);
        }

        var parameters = ImmutableArray.CreateBuilder<OpenApiParameterModel>();
        var bodyProperties = ImmutableArray.CreateBuilder<OpenApiRequestBodyPropertyModel>();

        foreach (var entry in ownershipData.Entries)
        {
            var (schemaType, schemaFormat) = MapClrTypeToOpenApiSchema(entry.PropertyTypeName);
            var constraints = OpenApiValidationConstraints.Extract(descriptor.OperationType, entry.PropertyName);
            var description = GetPropertyDescription(descriptor.OperationType, entry.PropertyName);
            var enumValues = GetEnumValues(descriptor.OperationType, entry.PropertyName);

            switch (entry.Source)
            {
                case PropertyBindingSource.Route:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "path",
                        Required: true,
                        schemaType,
                        schemaFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                    });
                    break;

                case PropertyBindingSource.Query:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "query",
                        Required: constraints.IsRequired,
                        schemaType,
                        schemaFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                    });
                    break;

                case PropertyBindingSource.Header:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "header",
                        Required: constraints.IsRequired,
                        schemaType,
                        schemaFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                    });
                    break;

                case PropertyBindingSource.Cookie:
                    parameters.Add(new OpenApiParameterModel(
                        entry.BindingName ?? entry.PropertyName,
                        "cookie",
                        Required: constraints.IsRequired,
                        schemaType,
                        schemaFormat)
                    {
                        Description = description,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                    });
                    break;

                case PropertyBindingSource.Body:
                    bodyProperties.Add(new OpenApiRequestBodyPropertyModel(
                        entry.BindingName ?? entry.PropertyName,
                        schemaType,
                        schemaFormat)
                    {
                        Description = description,
                        IsRequired = constraints.IsRequired,
                        MinLength = constraints.MinLength,
                        MaxLength = constraints.MaxLength,
                        Pattern = constraints.Pattern,
                        Minimum = constraints.Minimum,
                        Maximum = constraints.Maximum,
                        ExclusiveMinimum = constraints.ExclusiveMinimum,
                        ExclusiveMaximum = constraints.ExclusiveMaximum,
                        EnumValues = enumValues,
                        IsNullable = entry.IsNullable,
                    });
                    break;
            }
        }

        var orderedParameters = parameters
            .OrderBy(static p => p.In, StringComparer.Ordinal)
            .ThenBy(static p => p.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        var requestBody = bodyProperties.Count > 0
            ? new OpenApiRequestBodyModel(Required: true, bodyProperties.ToImmutableArray())
            : null;

        return (orderedParameters, requestBody);
    }

    /// <summary>
    /// Maps a fully-qualified CLR type name to the corresponding OpenAPI schema type and optional format.
    /// The supported types match the set validated by <c>PropertyOwnershipModule</c> at compile time.
    /// </summary>
    private static (string SchemaType, string? SchemaFormat) MapClrTypeToOpenApiSchema(string? clrTypeName)
    {
        return clrTypeName switch
        {
            "global::System.String" or "string" => ("string", null),
            "global::System.Int32" or "int" => ("integer", "int32"),
            "global::System.Int64" or "long" => ("integer", "int64"),
            "global::System.Int16" or "short" => ("integer", null),
            "global::System.Byte" or "byte" => ("integer", null),
            "global::System.Boolean" or "bool" => ("boolean", null),
            "global::System.Decimal" or "decimal" => ("number", "double"),
            "global::System.Double" or "double" => ("number", "double"),
            "global::System.Single" or "float" => ("number", "float"),
            "global::System.Guid" => ("string", "uuid"),
            "global::System.DateTime" => ("string", "date-time"),
            "global::System.DateTimeOffset" => ("string", "date-time"),
            _ => ("string", null),
        };
    }

    /// <summary>
    /// Extracts the <see cref="DescriptionAttribute"/> value from a property on the operation type,
    /// returning null when no description is specified.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "OpenAPI description projection reflects over operation type properties that are preserved by the source generator's generated pipeline code.")]
    private static string? GetPropertyDescription(Type operationType, string propertyName)
    {
        var property = operationType.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return property?
            .GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault()?.Description;
    }

    /// <summary>
    /// Extracts enum values when a property's CLR type is an enum, returning the defined names
    /// as an immutable array for OpenAPI <c>enum</c> rendering.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "OpenAPI enum projection reflects over operation type properties that are preserved by the source generator's generated pipeline code.")]
    private static ImmutableArray<string> GetEnumValues(Type operationType, string propertyName)
    {
        var property = operationType.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!propertyType.IsEnum)
        {
            return ImmutableArray<string>.Empty;
        }

        return Enum.GetNames(propertyType).ToImmutableArray();
    }
}

/// <summary>
/// Captures one rendered OpenAPI-style operation contract while preserving stable identity, ordered route formats, and
/// the previously rendered response objects.
/// </summary>
/// <param name="OperationName">The stable descriptor name for the operation.</param>
/// <param name="OperationTypeName">The CLR type name for the operation used as stable identity across later seams.</param>
/// <param name="HttpMethods">The deterministic HTTP method inventory published for the operation.</param>
/// <param name="UrlFormats">The deterministic route templates published for the operation.</param>
/// <param name="Tags">The deterministic OpenAPI tag inventory projected from descriptor-published group metadata.</param>
/// <param name="Responses">The rendered OpenAPI-style response objects for the operation.</param>
/// <param name="Parameters">The projected OpenAPI-style parameters from the property ownership plan.</param>
/// <param name="RequestBody">The projected OpenAPI-style request body from body-owned properties, or null when the operation has no body.</param>
public sealed record OpenApiOperationModel(
    string OperationName,
    string OperationTypeName,
    ImmutableArray<string> HttpMethods,
    ImmutableArray<string> UrlFormats,
    ImmutableArray<string> Tags,
    ImmutableArray<OpenApiResponseObjectModel> Responses,
    ImmutableArray<OpenApiParameterModel> Parameters,
    OpenApiRequestBodyModel? RequestBody)
{
    /// <summary>Gets the short summary for this operation in the OpenAPI output, or null when not specified.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets the longer description for this operation in the OpenAPI output, or null when not specified.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether this operation is deprecated in the OpenAPI output (maps from <see cref="ObsoleteAttribute"/>).</summary>
    public bool Deprecated { get; init; }

    /// <summary>Gets whether this operation requires authentication (maps from <see cref="AuthenticationRequirement.Authenticated"/>).</summary>
    public bool RequiresAuthentication { get; init; }
}
