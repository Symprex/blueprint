using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Xml;
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
    // Each assembly's described members (types and properties, the only members described) by their documentation id, or
    // null when it has no documentation file. Only what is read is kept, as the strings it is read as: the parsed file
    // was several times its size on disk, and most of it was the parameters, returns and methods nothing reads. The
    // entries are never changed: an inheritdoc is resolved on a copy.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, MemberDocs>?> _membersByAssembly = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the number of assemblies whose documentation is held.</summary>
    internal static int CachedAssemblyCount => _membersByAssembly.Count;

    /// <summary>Drops every held assembly's documentation; the next lookup reads it again.</summary>
    internal static void Release()
    {
        _membersByAssembly.Clear();
    }

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
        var tag = tagName == "summary" ? DocKind.Summary : DocKind.Remarks;
        var content = GetElement(member)?.Find(tag)?.GetContent(escapeText) ?? string.Empty;

        return content.Length == 0 ? null : content;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Legacy treated documentation it could not read, for whatever reason, as none.")]
    private static ResolvedDocs? GetElement(MemberInfo member)
    {
        try
        {
            var members = GetMembers(member.Module.Assembly);

            if (members is null || !members.TryGetValue(GetMemberElementName(member), out var docs))
            {
                return null;
            }

            var copy = new ResolvedDocs(docs);
            ReplaceInheritdocElements(member, copy);

            return copy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, MemberDocs>? GetMembers(Assembly assembly)
    {
        var assemblyName = assembly.GetName();

        if (string.IsNullOrEmpty(assemblyName.Name))
        {
            return null;
        }

        return _membersByAssembly.GetOrAdd(assemblyName.FullName, _ => LoadMembers(assembly, assemblyName.Name));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Legacy treated a documentation file it could not read as none.")]
    private static IReadOnlyDictionary<string, MemberDocs>? LoadMembers(Assembly assembly, string assemblyName)
    {
        var path = GetXmlDocsPath(assembly, assemblyName);

        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            // The file is read a member at a time, so no more of it than the member being kept is ever in memory.
            // Legacy loaded the whole document, so a file that is not well formed throughout has no documentation at all.
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null });
            var members = new Dictionary<string, MemberDocs>(StringComparer.Ordinal);

            reader.MoveToContent();

            if (reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement)
            {
                ReadMembers(reader, members);
            }

            while (reader.Read())
            {
            }

            return members;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The first "members" element of the root, as legacy's document.Root.Element("members") found it.
    private static void ReadMembers(XmlReader reader, Dictionary<string, MemberDocs> members)
    {
        var rootDepth = reader.Depth;
        var found = false;

        reader.Read();

        while (!found && reader.Depth > rootDepth)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == rootDepth + 1 && reader.Name == "members" && reader.NamespaceURI.Length == 0)
            {
                found = true;

                if (!reader.IsEmptyElement)
                {
                    var membersDepth = reader.Depth;

                    reader.Read();

                    while (reader.Depth > membersDepth)
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Depth == membersDepth + 1 && reader.Name == "member" && reader.NamespaceURI.Length == 0)
                        {
                            var element = (XElement)XNode.ReadFrom(reader);

                            // The first of a name is the one legacy's XPath query found.
                            if (element.Attribute("name")?.Value is { } name && IsDescribed(name))
                            {
                                members.TryAdd(name, MemberDocs.From(element));
                            }
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                }
            }
            else
            {
                reader.Skip();
            }
        }
    }

    // Only types and properties are described: GetMemberElementName writes no other prefix.
    private static bool IsDescribed(string name)
    {
        return name.StartsWith("T:", StringComparison.Ordinal) || name.StartsWith("P:", StringComparison.Ordinal);
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
    private static void ReplaceInheritdocElements(MemberInfo member, ResolvedDocs docs)
    {
        foreach (var child in docs.Nodes.Where(static n => n.Kind == DocKind.Inheritdoc).ToList())
        {
            var baseMember = member.DeclaringType!.GetTypeInfo().BaseType?.GetTypeInfo().DeclaredMembers.SingleOrDefault(m => m.Name == member.Name);
            var baseElement = baseMember is null ? null : GetElement(baseMember);

            if (baseElement is not null)
            {
                docs.Replace(child, baseElement.Nodes);
            }
            else
            {
                ReplaceInheritdocFromInterfaces(member, docs, child);
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Documentation of an interface member that has been trimmed away is none, as for any member without documentation.")]
    private static void ReplaceInheritdocFromInterfaces(MemberInfo member, ResolvedDocs docs, DocNode inheritdoc)
    {
        foreach (var implemented in member.DeclaringType!.GetTypeInfo().ImplementedInterfaces)
        {
            var interfaceMember = implemented.GetTypeInfo().DeclaredMembers.SingleOrDefault(m => m.Name == member.Name);
            var interfaceElement = interfaceMember is null ? null : GetElement(interfaceMember);

            if (interfaceElement is not null)
            {
                docs.Replace(inheritdoc, interfaceElement.Nodes);
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

    private enum DocKind
    {
        Summary,
        Remarks,
        Inheritdoc,
    }

    // A child of a member's documentation that is read: its content as each caller formats it, which depends on nothing
    // else, so the element need not be kept.
    private sealed class DocNode
    {
        private readonly string _plain;
        private readonly string _escaped;

        private DocNode(DocKind kind, string plain, string escaped)
        {
            this.Kind = kind;
            this._plain = plain;
            this._escaped = escaped;
        }

        public DocKind Kind { get; }

        public static DocNode? From(XElement element)
        {
            if (string.Equals(element.Name.LocalName, "inheritdoc", StringComparison.OrdinalIgnoreCase))
            {
                return new DocNode(DocKind.Inheritdoc, string.Empty, string.Empty);
            }

            var kind = element.Name == "summary" ? DocKind.Summary : element.Name == "remarks" ? DocKind.Remarks : (DocKind?)null;

            if (kind is null)
            {
                return null;
            }

            var plain = ToContent(element, escapeText: false);
            var escaped = ToContent(element, escapeText: true);

            // Most text has nothing to escape, so one string serves both.
            return new DocNode(kind.Value, plain, string.Equals(plain, escaped, StringComparison.Ordinal) ? plain : escaped);
        }

        public string GetContent(bool escapeText)
        {
            return escapeText ? this._escaped : this._plain;
        }
    }

    // What is read of one member's documentation: its summary, remarks and inheritdoc children, in order, as the first of
    // a kind is the one read.
    private sealed class MemberDocs
    {
        private static readonly MemberDocs _none = new([]);

        private MemberDocs(DocNode[] nodes)
        {
            this.Nodes = nodes;
        }

        public DocNode[] Nodes { get; }

        public static MemberDocs From(XElement member)
        {
            var nodes = member.Elements().Select(DocNode.From).OfType<DocNode>().ToArray();

            return nodes.Length == 0 ? _none : new MemberDocs(nodes);
        }
    }

    // A member's documentation with its inheritdoc children replaced, on a copy: the held MemberDocs are never changed.
    private sealed class ResolvedDocs
    {
        private readonly List<DocNode> _nodes;

        public ResolvedDocs(MemberDocs docs)
        {
            this._nodes = [.. docs.Nodes];
        }

        public IReadOnlyList<DocNode> Nodes => this._nodes;

        public DocNode? Find(DocKind kind)
        {
            return this._nodes.Find(n => n.Kind == kind);
        }

        // An inheritdoc is replaced once: a second replacement finds it gone, as replacing a node with no parent failed.
        public void Replace(DocNode child, IReadOnlyList<DocNode> with)
        {
            var index = this._nodes.IndexOf(child);

            if (index < 0)
            {
                throw new InvalidOperationException("The inheritdoc has already been replaced.");
            }

            this._nodes.RemoveAt(index);
            this._nodes.InsertRange(index, with);
        }
    }
}
