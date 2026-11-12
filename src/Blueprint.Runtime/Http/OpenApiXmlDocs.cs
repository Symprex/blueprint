using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Reads a type's or a property's <c>summary</c> from the XML documentation file beside the assembly that declares it,
/// formatted as legacy Blueprint's OpenAPI document formatted it, so the document describes what legacy described.
/// </summary>
/// <remarks>
/// <para>
/// Legacy took a schema's and a property's description from NJsonSchema, which read it through Namotion.Reflection, and a
/// parameter's through its own copy of Namotion's reader (<c>Blueprint.Utilities.XmlDocsExtensions</c>). Both read the
/// <c>{assembly}.xml</c> written by <c>GenerateDocumentationFile</c> beside the assembly, resolve an
/// <c>&lt;inheritdoc/&gt;</c> from the base type's or an interface's member of the same name, write a
/// <c>&lt;see cref&gt;</c> as the last segment of its target, a <c>&lt;see langword&gt;</c> as its word, a
/// <c>&lt;paramref&gt;</c> as its name and any other element as its text, and keep line breaks, removing the first line's
/// indentation from every line. The two differ only in text: Namotion writes it as it reads, legacy's copy as XML, with
/// <c>&amp;amp;</c> and <c>&amp;lt;</c> escaped.
/// </para>
/// <para>
/// A member whose documentation cannot be read, for whatever reason, has none, as legacy's did. An assembly whose
/// documentation file is not deployed beside it has none.
/// </para>
/// </remarks>
internal static class OpenApiXmlDocs
{
    // How many crefs one inheritdoc follows, so documentation whose crefs form a cycle still ends.
    private const int MaximumCrefDepth = 5;

    // Each assembly's documented members by their documentation id, or null when it has no documentation file. The
    // elements are never changed: an inheritdoc is resolved on a copy.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, XElement>?> _membersByAssembly = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the summary of <paramref name="member"/> as legacy described a schema or a property with it (through
    /// NJsonSchema), or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="member">The type or property.</param>
    /// <returns>The summary, or <see langword="null"/>.</returns>
    public static string? GetSchemaSummary(MemberInfo member)
    {
        return GetSummary(member, escapeText: false);
    }

    /// <summary>
    /// Returns the summary of <paramref name="member"/> as legacy described a parameter with it (through its own
    /// <c>XmlDocsExtensions</c>), or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="member">The property the parameter binds.</param>
    /// <returns>The summary, or <see langword="null"/>.</returns>
    public static string? GetParameterSummary(MemberInfo member)
    {
        return GetSummary(member, escapeText: true);
    }

    /// <summary>
    /// Returns the summary of an operation's type as legacy gave an operation its <c>summary</c> (through its own
    /// <c>XmlDocsExtensions</c>), or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="operationType">The operation's type.</param>
    /// <returns>The summary, or <see langword="null"/>.</returns>
    public static string? GetOperationSummary(Type operationType)
    {
        return GetTag(operationType, "summary", escapeText: true);
    }

    /// <summary>
    /// Returns the remarks of an operation's type as legacy gave an operation its <c>description</c> (through its own
    /// <c>XmlDocsExtensions</c>), or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="operationType">The operation's type.</param>
    /// <returns>The remarks, or <see langword="null"/>.</returns>
    public static string? GetOperationRemarks(Type operationType)
    {
        return GetTag(operationType, "remarks", escapeText: true);
    }

    private static string? GetSummary(MemberInfo member, bool escapeText)
    {
        return GetTag(member, "summary", escapeText);
    }

    private static string? GetTag(MemberInfo member, string tagName, bool escapeText)
    {
        var content = ToContent(GetElement(member)?.Element(tagName), escapeText);

        return content.Length == 0 ? null : content;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Legacy treated documentation it could not read, for whatever reason, as none.")]
    private static XElement? GetElement(MemberInfo member, int depth = 0)
    {
        try
        {
            var members = GetMembers(member.Module.Assembly);

            if (members is null || !members.TryGetValue(GetMemberElementName(member), out var element))
            {
                return null;
            }

            var copy = new XElement(element);
            ReplaceInheritdocElements(member, copy, depth);

            return copy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, XElement>? GetMembers(Assembly assembly)
    {
        var assemblyName = assembly.GetName();

        if (string.IsNullOrEmpty(assemblyName.Name))
        {
            return null;
        }

        return _membersByAssembly.GetOrAdd(assemblyName.FullName, _ => LoadMembers(assembly, assemblyName.Name));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Legacy treated a documentation file it could not read as none.")]
    private static IReadOnlyDictionary<string, XElement>? LoadMembers(Assembly assembly, string assemblyName)
    {
        var path = GetXmlDocsPath(assembly, assemblyName);

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            var members = new Dictionary<string, XElement>(StringComparer.Ordinal);

            foreach (var element in document.Root?.Element("members")?.Elements("member") ?? [])
            {
                // The first of a name is the one legacy's XPath query found.
                if (element.Attribute("name")?.Value is { } name)
                {
                    members.TryAdd(name, element);
                }
            }

            return members;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Beside the assembly, else in the base directory or its bin folder, else in the current directory or its bin
    // folder, as legacy looked.
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An assembly with no location, as in a single-file app, is looked for in the base directory, as legacy did.")]
    private static string? GetXmlDocsPath(Assembly assembly, string assemblyName)
    {
        var fileName = assemblyName + ".xml";

        if (!string.IsNullOrEmpty(assembly.Location) && Path.GetDirectoryName(assembly.Location) is { } assemblyDirectory)
        {
            var besideAssembly = Path.Combine(assemblyDirectory, fileName);

            if (File.Exists(besideAssembly))
            {
                return besideAssembly;
            }
        }

        var baseDirectory = AppContext.BaseDirectory;

        if (!string.IsNullOrEmpty(baseDirectory))
        {
            var inBaseDirectory = Path.Combine(baseDirectory, fileName);

            return File.Exists(inBaseDirectory) ? inBaseDirectory : Path.Combine(baseDirectory, "bin", fileName);
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        var inCurrentDirectory = Path.Combine(currentDirectory, fileName);

        return File.Exists(inCurrentDirectory) ? inCurrentDirectory : Path.Combine(currentDirectory, "bin", fileName);
    }

    // An <inheritdoc/> anywhere in a member's documentation takes what the member it inherits from documents: the member its
    // cref names or, without a cref or when that names nothing usable, the base type's member of the same name, or failing
    // that an interface's. One that stands alone takes each tag the member does not document itself; one inside a tag, such
    // as <summary>, takes that tag's content. A property [Expose<TInner>] contributes carries the inner property's cref,
    // which no base type or interface of the gateway operation has (Symprex/blueprint#174). One that inherits nothing stays,
    // and reads as nothing, so the rest of the documentation is still used.
    private static void ReplaceInheritdocElements(MemberInfo member, XElement element, int depth)
    {
        foreach (var inheritdoc in element.Descendants().Where(IsInheritdoc).ToList())
        {
            if (GetInheritedElement(member, inheritdoc, depth) is { } inherited)
            {
                ReplaceWithInherited(element, inheritdoc, inherited);
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Documentation of a base or interface member that has been trimmed away is none, as for any member without documentation.")]
    private static XElement? GetInheritedElement(MemberInfo member, XElement inheritdoc, int depth)
    {
        if (inheritdoc.Attribute("cref")?.Value is { Length: > 2 } cref && GetCrefElement(member.Module.Assembly, cref, depth) is { } named)
        {
            return named;
        }

        // A type has no member of its own name on its base to inherit from.
        if (member is Type)
        {
            return null;
        }

        var baseMember = member.DeclaringType!.GetTypeInfo().BaseType?.GetTypeInfo().DeclaredMembers.SingleOrDefault(m => m.Name == member.Name);

        if (baseMember is not null && GetElement(baseMember, depth) is { } baseElement && IsUsable(baseElement))
        {
            return baseElement;
        }

        foreach (var implemented in member.DeclaringType!.GetTypeInfo().ImplementedInterfaces)
        {
            var interfaceMember = implemented.GetTypeInfo().DeclaredMembers.SingleOrDefault(m => m.Name == member.Name);

            if (interfaceMember is not null && GetElement(interfaceMember, depth) is { } interfaceElement && IsUsable(interfaceElement))
            {
                return interfaceElement;
            }
        }

        return null;
    }

    /// <summary>
    /// The documentation of the member a cref names, as the compiler writes it (<c>P:Ns.Type.Property</c>), looked for in the
    /// documentation of <paramref name="assembly"/> and then of each assembly it references: an inner operation is usually
    /// in an upstream contract's assembly, whose file the gateway's does not repeat. The named member's own inheritdocs are
    /// resolved as its own: a cref-less one from its base type or an interface, in the assembly that declares it, and one
    /// with a cref in turn, each cref one step deeper.
    /// </summary>
    /// <returns>
    /// The documentation, or <see langword="null"/> past the depth limit, for a member no documentation has, or for one
    /// whose documentation has nothing usable once resolved, so the caller looks elsewhere.
    /// </returns>
    private static XElement? GetCrefElement(Assembly assembly, string cref, int depth)
    {
        if (depth >= MaximumCrefDepth || FindDocumentedMember(assembly, cref) is not ({ } target, { } targetAssembly))
        {
            return null;
        }

        XElement? resolved;

        if (FindMember(targetAssembly, cref) is { } targetMember)
        {
            resolved = GetElement(targetMember, depth + 1);
        }
        else
        {
            // A member reflection is not used to find, such as a method: only the crefs of its inheritdocs can be followed.
            resolved = new XElement(target);

            foreach (var nested in resolved.Descendants().Where(IsInheritdoc).ToList())
            {
                if (nested.Attribute("cref")?.Value is { Length: > 2 } nestedCref && GetCrefElement(targetAssembly, nestedCref, depth + 1) is { } inherited)
                {
                    ReplaceWithInherited(resolved, nested, inherited);
                }
            }
        }

        return resolved is not null && IsUsable(resolved) ? resolved : null;
    }

    // An inheritdoc directly in the member's documentation takes each of the inherited tags the member does not document
    // itself; one inside a tag takes the inherited tag of the same name's content.
    private static void ReplaceWithInherited(XElement documentation, XElement inheritdoc, XElement inherited)
    {
        var nodes = inheritdoc.Parent == documentation
            ? inherited.Elements().Where(tag => documentation.Element(tag.Name) is null).ToArray<XNode>()
            : inherited.Element(inheritdoc.Parent!.Name)?.Nodes().ToArray() ?? [];

        if (nodes.Length > 0)
        {
            inheritdoc.ReplaceWith(nodes);
        }
    }

    // Documentation with something to describe once its unresolved inheritdocs, which read as nothing, are set aside.
    private static bool IsUsable(XElement documentation)
    {
        return documentation.DescendantNodes().Any(static node => node switch
        {
            XText text => !string.IsNullOrWhiteSpace(text.Value),
            XElement element => !IsInheritdoc(element) && element.HasAttributes,
            _ => false,
        });
    }

    private static bool IsInheritdoc(XElement element)
    {
        return string.Equals(element.Name.LocalName, "inheritdoc", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The type or property a documentation id names (<c>T:Ns.Outer.Nested</c>, <c>P:Ns.Type.Property</c>) in
    /// <paramref name="assembly"/>, or <see langword="null"/> for any other kind of member, or one it cannot find.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "A property that has been trimmed away has no documentation to inherit, as for any member without documentation.")]
    private static MemberInfo? FindMember(Assembly assembly, string documentationId)
    {
        var name = documentationId[2..];

        switch (documentationId[0])
        {
            case 'T':
                return FindType(assembly, name);

            case 'P' when name.LastIndexOf('.') is > 0 and var lastDot:
                return FindType(assembly, name[..lastDot])?.GetProperty(
                    name[(lastDot + 1)..],
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

            default:
                return null;
        }
    }

    // A documentation id writes a nested type's '+' as '.', so each '.' from the right may be either.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "A type that has been trimmed away has no documentation to inherit, as for any member without documentation.")]
    private static Type? FindType(Assembly assembly, string name)
    {
        var candidate = name;

        while (true)
        {
            if (assembly.GetType(candidate) is { } type)
            {
                return type;
            }

            var lastDot = candidate.LastIndexOf('.');

            if (lastDot < 0)
            {
                return null;
            }

            candidate = string.Concat(candidate.AsSpan(0, lastDot), "+", candidate.AsSpan(lastDot + 1));
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A referenced assembly that cannot be loaded has no documentation, as for any assembly without a documentation file.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "A reference that has been trimmed away has no documentation, as for any assembly without a documentation file; the lookup only builds the OpenAPI document.")]
    private static (XElement? Element, Assembly? Assembly) FindDocumentedMember(Assembly assembly, string documentationId)
    {
        if (GetMembers(assembly) is { } own && own.TryGetValue(documentationId, out var ownElement))
        {
            return (ownElement, assembly);
        }

        foreach (var referencedName in assembly.GetReferencedAssemblies())
        {
            Assembly referenced;

            try
            {
                referenced = Assembly.Load(referencedName);
            }
            catch (Exception)
            {
                continue;
            }

            if (GetMembers(referenced) is { } members && members.TryGetValue(documentationId, out var element))
            {
                return (element, referenced);
            }
        }

        return (null, null);
    }

    private static string ToContent(XElement? element, bool escapeText)
    {
        if (element is null)
        {
            return string.Empty;
        }

        var value = new StringBuilder();

        foreach (var node in element.Nodes())
        {
            if (node is XElement e)
            {
                if (e.Name == "see")
                {
                    if (e.Attribute("langword") is { } langword)
                    {
                        value.Append(langword.Value);
                    }
                    else if (!string.IsNullOrEmpty(e.Value))
                    {
                        value.Append(e.Value);
                    }
                    else if (e.Attribute("cref") is { } cref)
                    {
                        value.Append(cref.Value.Trim('!', ':').Trim().Split('.').Last());
                    }
                    else if (e.Attribute("href") is { } href)
                    {
                        value.Append(href.Value);
                    }
                }
                else if (e.Name == "paramref")
                {
                    value.Append(e.Attribute("name")?.Value ?? e.Value);
                }
                else
                {
                    value.Append(e.Value);
                }
            }
            else
            {
                value.Append(node is XText text && !escapeText ? text.Value : node.ToString());
            }
        }

        return RemoveLineBreakWhiteSpaces(value.ToString());
    }

    // Removes the first line's indentation from the start of every line, and the line breaks at either end.
    private static string RemoveLineBreakWhiteSpaces(string documentation)
    {
        if (string.IsNullOrEmpty(documentation))
        {
            return string.Empty;
        }

        documentation = "\n" + documentation.Replace("\r", string.Empty, StringComparison.Ordinal).Trim('\n');

        var i = 1;

        for (; i < documentation.Length; i++)
        {
            if (documentation[i] != ' ')
            {
                break;
            }
        }

        documentation = documentation.Replace($"\n{new string(' ', i - 1)}", "\n", StringComparison.Ordinal);

        return documentation.Trim('\n');
    }

    // The documentation id of a type or a property, as the compiler writes it: "T:Namespace.Outer.Nested" or
    // "P:Namespace.Type.Property", a generic type by its definition ("T:Namespace.List`1").
    private static string GetMemberElementName(MemberInfo member)
    {
        char prefix;
        string memberName;

        switch (member)
        {
            case Type type:
                prefix = 'T';
                memberName = type.FullName!.Split('[')[0];
                break;

            case PropertyInfo property:
                prefix = 'P';
                memberName = property.DeclaringType!.FullName!.Split('[')[0] + "." + property.Name;
                break;

            default:
                throw new ArgumentException("Only a type's or a property's documentation is read.", nameof(member));
        }

        return $"{prefix}:{memberName.Replace('+', '.')}";
    }
}
