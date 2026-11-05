using Blueprint.Model;
using Blueprint.Model.Attributes;

// A namespace of its own, so the type named Http is not what "Http" means to the fixtures that name the
// Blueprint.SourceGenerator.Tests.Integration.Http namespace by its last segment.
namespace Blueprint.SourceGenerator.Tests.Integration.Http.TypedRequestBuilderNames;

/// <summary>A query named <c>Http</c>, which cannot declare a nested builder class of its own name.</summary>
[RootLink("/typed-builder/http/{Id}")]
public partial class Http : IQuery<string>
{
    public string? Id { get; set; }

    public static string Handle(Http query)
    {
        return query.Id ?? "(unset)";
    }
}
