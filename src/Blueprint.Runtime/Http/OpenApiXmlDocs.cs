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
    private static XElement? GetElement(MemberInfo member)
    {
        try
        {
            var members = GetMembers(member.Module.Assembly);

            if (members is null || !members.TryGetValue(GetMemberElementName(member), out var element))
            {
                return null;
            }

            var copy = new XElement(element);
            ReplaceInheritdocElements(member, copy);

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

    // An <inheritdoc/> takes every node of the base type's member of the same name, or failing that an interface's.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Documentation of a base or interface member that has been trimmed away is none, as for any member without documentation.")]
    private static void ReplaceInheritdocElements(MemberInfo member, XElement element)
    {
        foreach (var child in element.Nodes().ToList().OfType<XElement>())
        {
            if (!string.Equals(child.Name.LocalName, "inheritdoc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var baseMember = member.DeclaringType!.GetTypeInfo().BaseType?.GetTypeInfo().DeclaredMembers.SingleOrDefault(m => m.Name == member.Name);
            var baseElement = baseMember is null ? null : GetElement(baseMember);

            if (baseElement is not null)
            {
                child.ReplaceWith(baseElement.Nodes().ToArray());
            }
            else
            {
                ReplaceInheritdocFromInterfaces(member, child);
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Documentation of an interface member that has been trimmed away is none, as for any member without documentation.")]
    private static void ReplaceInheritdocFromInterfaces(MemberInfo member, XElement inheritdoc)
    {
        foreach (var implemented in member.DeclaringType!.GetTypeInfo().ImplementedInterfaces)
        {
            var interfaceMember = implemented.GetTypeInfo().DeclaredMembers.SingleOrDefault(m => m.Name == member.Name);
            var interfaceElement = interfaceMember is null ? null : GetElement(interfaceMember);

            if (interfaceElement is not null)
            {
                inheritdoc.ReplaceWith(interfaceElement.Nodes().ToArray());
            }
        }
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
