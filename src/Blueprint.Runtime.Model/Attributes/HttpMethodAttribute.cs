namespace Blueprint.Model.Attributes;

/// <summary>
/// Overrides the conventional HTTP method for the operation with the given HTTP method.
/// </summary>
/// <param name="httpMethod">The HTTP method to use for the operation.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class HttpMethodAttribute(string httpMethod) : Attribute
{
    /// <summary>
    /// Gets the explicit HTTP method that should be used instead of Blueprint's conventional inference.
    /// </summary>
    public string HttpMethod { get; } = httpMethod;
}

/// <summary>
/// Overrides the conventional HTTP method for the operation, forcing a GET request.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class HttpGetAttribute() : HttpMethodAttribute("GET");

/// <summary>
/// Overrides the conventional HTTP method for the operation, forcing a POST request.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class HttpPostAttribute() : HttpMethodAttribute("POST");

/// <summary>
/// Overrides the conventional HTTP method for the operation, forcing a PUT request.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class HttpPutAttribute() : HttpMethodAttribute("PUT");

/// <summary>
/// Overrides the conventional HTTP method for the operation, forcing a PATCH request.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class HttpPatchAttribute() : HttpMethodAttribute("PATCH");

/// <summary>
/// Overrides the conventional HTTP method for the operation, forcing a DELETE request.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class HttpDeleteAttribute() : HttpMethodAttribute("DELETE");
