using System.Text.RegularExpressions;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0008
#pragma warning disable IDE0079
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a generated class or struct.
    /// Create it from <see cref="CodeBuilder"/>, then attach members, inheritance, and XML documentation before rendering the owning file.
    /// </summary>
    public sealed class ClassBuilder : BuilderBase<ClassBuilder>
    {
        private readonly List<string> _attributes = new List<string>();
        private readonly List<string> _interfaces = new List<string>();
        private readonly List<string> _classAttributes = new List<string>();
        private readonly List<ConstructorBuilder> _constructors = new List<ConstructorBuilder>();
        private readonly List<EventBuilder> _events = new List<EventBuilder>();
        private readonly List<PropertyBuilder> _properties = new List<PropertyBuilder>();
        private readonly List<MethodBuilder> _methods = new List<MethodBuilder>();
        private readonly Queue<ClassBuilder> _nestedClass = new Queue<ClassBuilder>();
        private readonly Queue<DelegateBuilder> _nestedDelegates = new Queue<DelegateBuilder>();
        private readonly GenericCollection _generics = new GenericCollection();
        private readonly bool _isPartial;
        private DocumentationComment? _xmlDoc;
        private Func<PropertyBuilder, string>? _propertiesOrderBy = null;

        /// <summary>
        /// Initializes a new instance attached to the supplied <see cref="CodeBuilder"/>.
        /// </summary>
        /// <param name="className">The type name to emit.</param>
        /// <param name="codeBuilder">The file builder that owns this declaration.</param>
        /// <param name="partial"><see langword="true"/> to emit the <c>partial</c> modifier; otherwise <see langword="false"/>.</param>
        internal ClassBuilder(string className, CodeBuilder codeBuilder, bool partial = true)
        {
            this.Name = className;
            this.Builder = codeBuilder;
            this._isPartial = partial;
        }

        /// <summary>
        /// Gets the type name that will be emitted for the generated declaration.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the fully qualified type name composed from the owning namespace and the generated type name.
        /// </summary>
        public string FullyQualifiedName => $"{this.Builder.Namespace}.{this.Name}";

        /// <summary>
        /// Gets the constructors currently configured for the generated type.
        /// </summary>
        public IReadOnlyList<ConstructorBuilder> Constructors => this._constructors;

        /// <summary>
        /// Gets the properties currently configured for the generated type.
        /// </summary>
        public IReadOnlyList<PropertyBuilder> Properties => this._properties;

        /// <summary>
        /// Gets the methods currently configured for the generated type.
        /// </summary>
        public IReadOnlyList<MethodBuilder> Methods => this._methods;

        /// <summary>
        /// Gets the nested types currently configured for the generated type.
        /// </summary>
        public IReadOnlyList<ClassBuilder> NestedClasses => this._nestedClass.ToList();

        /// <summary>
        /// Gets the root CodeBuilder that owns this generated type.
        /// </summary>
        public CodeBuilder Builder { get; internal set; }

        /// <summary>
        /// Gets the base type currently configured for the generated type.
        /// </summary>
        public string? BaseClass { get; private set; }

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated type.
        /// </summary>
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Gets whether the generated declaration will be emitted as a class or struct.
        /// </summary>
        public TypeKind Kind { get; private set; } = TypeKind.Class;

        /// <summary>
        /// Gets a value indicating whether the generated type will be emitted with the static modifier.
        /// </summary>
        public bool IsStatic { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the generated type will be emitted with the abstract modifier.
        /// </summary>
        public bool IsAbstract { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the generated type will be emitted with the sealed modifier.
        /// </summary>
        public bool IsSealed { get; private set; }

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated type.
        /// </summary>
        /// <param name="summary">The text to place inside the emitted summary documentation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder WithSummary(string summary)
        {
            this._xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures the generated type to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated type. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder WithInheritDoc(bool inherit = true)
        {
            this._xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder WithInheritDoc(string from)
        {
            this._xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Marks the generated type as sealed.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder Sealed()
        {
            this.IsSealed = true;
            return this;
        }

        /// <summary>
        /// Switches the generated declaration from class emission to struct emission.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder IsStruct()
        {
            this.Kind = TypeKind.Struct;
            return this;
        }

        /// <summary>
        /// Sets the Roslyn declaration kind used when the type is rendered.
        /// </summary>
        /// <param name="kind">The declaration kind to emit for the generated type.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder OfType(TypeKind kind)
        {
            this.Kind = kind;
            return this;
        }

        /// <summary>
        /// Sets the base type that the generated class should inherit from.
        /// </summary>
        /// <param name="baseClass">The base type that the generated class should inherit from.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder SetBaseClass(string baseClass)
        {
            this.BaseClass = baseClass;

            if (this.BaseClass.Contains("."))
            {
                var className = baseClass.Split('.').Last();
                this.AddNamespaceImport(Regex.Replace(baseClass, @$"\.{className}", string.Empty));
            }

            return this;
        }

        /// <summary>
        /// Sets the base type that the generated class should inherit from.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder SetBaseClass(INamedTypeSymbol symbol)
        {
            if (symbol.Name == this.Name)
            {
                this.BaseClass = $"global::{symbol.GetFullMetadataName()}";
                return this;
            }

            this.BaseClass = symbol.Name;
            return this.AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Adds a generic type parameter to the generated type and optionally configures its constraints.
        /// </summary>
        /// <param name="name">The generated name to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddGeneric(string name)
        {
            return this.AddGeneric(name, _ => { });
        }

        /// <summary>
        /// Adds a generic type parameter to the generated type and optionally configures its constraints.
        /// </summary>
        /// <param name="name">The generated name to emit.</param>
        /// <param name="configureBuilder">A callback that can further configure the newly created builder before it is attached.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
        {
            if (configureBuilder is null)
            {
                throw new ArgumentNullException(nameof(configureBuilder));
            }
            else if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }
            else if (this._generics.Any(x => x.Name == name))
            {
                throw new ArgumentException($"The argument {name} already exists");
            }

            var builder = new GenericBuilder(name);
            configureBuilder(builder);
            this._generics.Add(builder);
            return this;
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this type.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ClassBuilder AddAssemblyAttribute(string attribute)
        {
            this.Builder.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds an attribute to the generated type if it is not already present.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!this._attributes.Contains(sanitized))
            {
                this._attributes.Add(sanitized);
            }

            return this;
        }

        /// <summary>
        /// Adds namespace import to the current generated type.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ClassBuilder AddNamespaceImport(string importedNamespace)
        {
            this.Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import to the current generated type.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ClassBuilder AddNamespaceImport(ISymbol symbol)
        {
            this.Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds namespace import to the current generated type.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override ClassBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            this.Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds an implemented interface to the generated type.
        /// </summary>
        /// <param name="interfaceName">The interface name to add to the generated type.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddInterface(string interfaceName)
        {
            this._interfaces.Add(interfaceName);

            if (interfaceName.Contains('.'))
            {
                var name = interfaceName.Split('.').Last();
                return this.AddNamespaceImport(Regex.Replace(interfaceName, @$"\.{name}$", string.Empty));
            }

            return this;
        }

        /// <summary>
        /// Adds an implemented interface to the generated type.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddInterface(ITypeSymbol symbol)
        {
            this._interfaces.Add(symbol.Name);
            return this.AddNamespaceImport(symbol);
        }

        /// <summary>
        /// Adds one or more implemented interfaces to the generated type.
        /// </summary>
        /// <param name="interfaces">The interface names to add to the generated type.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddInterfaces(IEnumerable<string> interfaces)
        {
            if (interfaces is null)
            {
                return this;
            }

            foreach (var interfaceName in interfaces)
            {
                this.AddInterface(interfaceName);
            }

            return this;
        }

        /// <summary>
        /// Adds one or more implemented interfaces to the generated type.
        /// </summary>
        /// <param name="interfaces">The interface names to add to the generated type.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddInterfaces(IEnumerable<INamedTypeSymbol> interfaces)
        {
            if (interfaces is null)
            {
                return this;
            }

            foreach (var symbol in interfaces)
            {
                this.AddInterface(symbol);
            }

            return this;
        }

        /// <summary>
        /// Adds a constructor to the generated type and returns a builder for its signature and body.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public ConstructorBuilder AddConstructor(Accessibility? accessModifier = null)
        {
            var builder = new ConstructorBuilder(accessModifier, this);
            this._constructors.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds a constructor to the generated type and returns a builder for its signature and body.
        /// </summary>
        /// <param name="baseConstructor">The base constructor used by this operation.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public ConstructorBuilder AddConstructor(IMethodSymbol baseConstructor, Accessibility? accessModifier = null)
        {
            var builder = this.AddConstructor(accessModifier)
                .AddParameters(baseConstructor.Parameters);
            this._constructors.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds a method to the generated type and returns a builder for further configuration.
        /// </summary>
        /// <param name="name">The method name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public MethodBuilder AddMethod(string name, Accessibility? accessModifier = null)
        {
            var builder = new MethodBuilder(name, accessModifier, this);
            this._methods.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds a property or field declaration to the generated type and returns a builder for further configuration.
        /// </summary>
        /// <param name="name">The property or field name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public PropertyBuilder AddProperty(string name, Accessibility? accessModifier = null)
        {
            var builder = new PropertyBuilder(name, accessModifier, this);
            this._properties.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds an event declaration to the generated type and returns a builder for further configuration.
        /// </summary>
        /// <param name="eventName">The event name used by this operation.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public EventBuilder AddEvent(string eventName)
        {
            var builder = new EventBuilder(this, eventName);
            this._events.Add(builder);
            return builder;
        }

        /// <summary>
        /// Sets the generated type to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder MakePublicClass()
        {
            return this.WithAccessModifier(Accessibility.Public);
        }

        /// <summary>
        /// Sets the generated type to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder MakeInternalClass()
        {
            return this.WithAccessModifier(Accessibility.Internal);
        }

        /// <summary>
        /// Sets the accessibility emitted for the generated type.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder WithAccessModifier(Accessibility accessModifier)
        {
            this.AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Marks the generated type as static.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder MakeStaticClass()
        {
            this.IsStatic = true;
            return this;
        }

        /// <summary>
        /// Sets whether the generated type should be emitted with the abstract modifier.
        /// </summary>
        /// <param name="isAbstract">True to emit the generated declaration as abstract; otherwise false.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder Abstract(bool isAbstract = true)
        {
            this.IsAbstract = isAbstract;
            return this;
        }

        /// <summary>
        /// Adds a nested class to the generated type and returns its builder.
        /// </summary>
        /// <param name="name">The nested type name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddNestedClass(string name, Accessibility? accessModifier = null)
        {
            return this.AddNestedClass(name, false, accessModifier);
        }

        /// <summary>
        /// Adds a nested class to the generated type and returns its builder.
        /// </summary>
        /// <param name="name">The nested type name to emit.</param>
        /// <param name="partial">The partial used by this operation.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder AddNestedClass(string name, bool partial, Accessibility? accessModifier = null)
        {
            var builder = new ClassBuilder(name, this.Builder, partial);
            if (accessModifier.HasValue)
            {
                builder.WithAccessModifier(accessModifier.Value);
            }

            this._nestedClass.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Adds a nested delegate to the generated type and returns its builder.
        /// </summary>
        /// <param name="name">The nested delegate name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The builder for the newly added generated declaration.</returns>
        public DelegateBuilder AddNestedDelegate(string name, Accessibility? accessModifier = null)
        {
            var builder = new DelegateBuilder(name, this.Builder);
            if (accessModifier.HasValue)
            {
                builder.WithAccessModifier(accessModifier.Value);
            }

            this._nestedDelegates.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Renders the owning CodeBuilder and returns the generated C# source text.
        /// </summary>
        /// <returns>The rendered C# source text.</returns>
        public string Build()
        {
            return this.Builder.Build();
        }

        /// <summary>
        /// Preserves property insertion order instead of sorting generated properties by name.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder DontSortPropertiesByName()
        {
            this._propertiesOrderBy = _ => "";
            return this;
        }

        /// <inheritdoc/>
        internal override void Write(in CodeWriter writer)
        {
            this._xmlDoc?.Write(writer);

            WriteClassAttributes(this._classAttributes, writer);

            var staticDeclaration = this.IsStatic ? "static " : string.Empty;

            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(this.BaseClass))
            {
                queue.Enqueue(this.BaseClass!);
            }

            foreach (var inter in this._interfaces.Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x))
            {
                queue.Enqueue(inter);
            }

            var extra = queue.Any() ? $": {string.Join(", ", queue)}" : string.Empty;

            foreach (var attr in this._attributes)
            {
                writer.AppendLine($"[{attr}]");
            }

            if (this._methods.Any(x => x.IsAbstract))
            {
                this.IsAbstract = true;
            }

            var classDeclaration = new[] {
                this.AccessModifier.Code(),
                this.IsStatic ? "static" : null,
                this.IsSealed ? "sealed" : null,
                this.IsAbstract ? "abstract" : null,
                this._isPartial ? "partial" : null,
                this.Kind.ToString().ToLowerInvariant(),
                $"{this.Name}{this._generics}",
                extra
            };

            using (writer.Block(string.Join(" ", classDeclaration.Where(x => !string.IsNullOrEmpty(x))), this._generics.Contraints()))
            {
                var hadOutput = false;
                hadOutput = InvokeBuilderWrite(this._nestedDelegates, ref hadOutput, in writer);
                hadOutput = InvokeBuilderWrite(this._events, ref hadOutput, writer);
                var orderBy = this._propertiesOrderBy ?? (x => x.Name);
                hadOutput = InvokeBuilderWrite(
                    this._properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Const && x.IsStatic == false)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(
                    this._properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Const && x.IsStatic == true)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(
                    this._properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.ReadOnly)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(
                    this._properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Default)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(this._constructors.OrderBy(x => x.Parameters.Count), ref hadOutput, writer);
                hadOutput = InvokeBuilderWrite(
                    this._properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Property)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer);
                hadOutput = InvokeBuilderWrite(
                    this._methods.OrderBy(x => x.Name)
                        .ThenBy(x => x.Parameters.Count),
                    ref hadOutput,
                    writer);
                InvokeBuilderWrite(this._nestedClass, ref hadOutput, writer);
            }
        }

        private static void WriteClassAttributes(IEnumerable<string> classAttributes, in CodeWriter writer)
        {
            foreach (var attr in classAttributes.Distinct().OrderBy(x => x))
            {
                writer.AppendLine(attr);
            }
        }

        private static bool InvokeBuilderWrite<T>(IEnumerable<T> builders, ref bool hadOutput, in CodeWriter writer, bool group = false)
            where T : IBuilder
        {
            if (builders is null || !builders.Any())
            {
                return hadOutput;
            }

            if (hadOutput)
            {
                writer.NewLine();
            }

            var queue = new Queue<T>();
            foreach (var builder in builders)
            {
                queue.Enqueue(builder);
            }

            while (queue.Any())
            {
                var builder = queue.Dequeue();
                builder.Write(writer);

                if (!group && queue.Any())
                {
                    writer.NewLine();
                }
            }

            return true;
        }
    }
}
