using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Builds class or struct declarations and their members.
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

        internal ClassBuilder(string className, CodeBuilder codeBuilder, bool partial = true)
        {
            Name = className;
            Builder = codeBuilder;
            _isPartial = partial;
        }

        public string Name { get; }

        public string FullyQualifiedName => $"{Builder.Namespace}.{Name}";

        public IReadOnlyList<ConstructorBuilder> Constructors => _constructors;

        public IReadOnlyList<PropertyBuilder> Properties => _properties;

        public IReadOnlyList<MethodBuilder> Methods => _methods;

        public IReadOnlyList<ClassBuilder> NestedClasses => _nestedClass.ToList();

        public CodeBuilder Builder { get; internal set; }

        public string? BaseClass { get; private set; }

        public Accessibility? AccessModifier { get; private set; }

        public TypeKind Kind { get; private set; } = TypeKind.Class;

        public bool IsStatic { get; private set; }

        public bool IsAbstract { get; private set; }

        public bool IsSealed { get; private set; }

        /// <summary>
        /// Sets an XML summary comment for the generated type.
        /// </summary>
        /// <param name="summary">The summary value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithSummary(string summary)
        {
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures <inheritdoc/> XML documentation for the generated type.
        /// </summary>
        /// <param name="inherit">The inherit value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures <inheritdoc/> XML documentation for the generated type.
        /// </summary>
        /// <param name="from">The from value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Marks the generated type as sealed.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder Sealed()
        {
            IsSealed = true;
            return this;
        }

        /// <summary>
        /// Marks the generated declaration as a struct.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder IsStruct()
        {
            Kind = TypeKind.Struct;
            return this;
        }

        /// <summary>
        /// Sets the generated declaration kind.
        /// </summary>
        /// <param name="kind">The kind value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder OfType(TypeKind kind)
        {
            Kind = kind;
            return this;
        }

        /// <summary>
        /// Sets base class.
        /// </summary>
        /// <param name="baseClass">The base class value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder SetBaseClass(string baseClass)
        {
            BaseClass = baseClass;

            if(BaseClass.Contains("."))
            {
                var className = baseClass.Split('.').Last();
                AddNamespaceImport(Regex.Replace(baseClass, @$"\.{className}", string.Empty));
            }

            return this;
        }

        /// <summary>
        /// Sets base class.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder SetBaseClass(INamedTypeSymbol symbol)
        {
            if(symbol.Name == Name)
            {
                BaseClass = $"global::{symbol.GetFullMetadataName()}";
                return this;
            }

            BaseClass = symbol.Name;
            return AddNamespaceImport(symbol.ContainingNamespace);
        }

        /// <summary>
        /// Adds generic.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddGeneric(string name) =>
            AddGeneric(name, _ => { });

        /// <summary>
        /// Adds generic.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="configureBuilder">The configure builder value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
        {
            if(configureBuilder is null)
                throw new ArgumentNullException(nameof(configureBuilder));
            else if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            else if (_generics.Any(x => x.Name == name))
                throw new ArgumentException($"The argument {name} already exists");

            var builder = new GenericBuilder(name);
            configureBuilder(builder);
            _generics.Add(builder);
            return this;
        }

        /// <summary>
        /// Adds assembly attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public override ClassBuilder AddAssemblyAttribute(string attribute)
        {
            Builder.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public override ClassBuilder AddNamespaceImport(string importedNamespace)
        {
            Builder.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override ClassBuilder AddNamespaceImport(ISymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override ClassBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            Builder.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds interface.
        /// </summary>
        /// <param name="interfaceName">The interface name value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddInterface(string interfaceName)
        {
            _interfaces.Add(interfaceName);

            if (interfaceName.Contains('.'))
            {
                var name = interfaceName.Split('.').Last();
                return AddNamespaceImport(Regex.Replace(interfaceName, @$"\.{name}$", string.Empty));
            }

            return this;
        }

        /// <summary>
        /// Adds interface.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddInterface(ITypeSymbol symbol)
        {
            _interfaces.Add(symbol.Name);
            return AddNamespaceImport(symbol);
        }

        /// <summary>
        /// Adds interfaces.
        /// </summary>
        /// <param name="interfaces">The interfaces value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddInterfaces(IEnumerable<string> interfaces)
        {
            if (interfaces is null)
                return this;

            foreach (var interfaceName in interfaces)
                AddInterface(interfaceName);

            return this;
        }

        /// <summary>
        /// Adds interfaces.
        /// </summary>
        /// <param name="interfaces">The interfaces value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddInterfaces(IEnumerable<INamedTypeSymbol> interfaces)
        {
            if (interfaces is null)
                return this;

            foreach (var symbol in interfaces)
                AddInterface(symbol);

            return this;
        }

        /// <summary>
        /// Adds constructor.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder AddConstructor(Accessibility? accessModifier = null)
        {
            var builder = new ConstructorBuilder(accessModifier, this);
            _constructors.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds constructor.
        /// </summary>
        /// <param name="baseConstructor">The base constructor value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public ConstructorBuilder AddConstructor(IMethodSymbol baseConstructor, Accessibility? accessModifier = null)
        {
            var builder = AddConstructor(accessModifier)
                .AddParameters(baseConstructor.Parameters);
            _constructors.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds method.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public MethodBuilder AddMethod(string name, Accessibility? accessModifier = null)
        {
            var builder = new MethodBuilder(name, accessModifier, this);
            _methods.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds property.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder AddProperty(string name, Accessibility? accessModifier = null)
        {
            var builder = new PropertyBuilder(name, accessModifier, this);
            _properties.Add(builder);
            return builder;
        }

        /// <summary>
        /// Adds event.
        /// </summary>
        /// <param name="eventName">The event name value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder AddEvent(string eventName)
        {
            var builder = new EventBuilder(this, eventName);
            _events.Add(builder);
            return builder;
        }

        /// <summary>
        /// Marks public class.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder MakePublicClass() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Marks internal class.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder MakeInternalClass() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Configures access modifier.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Marks static class.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder MakeStaticClass()
        {
            IsStatic = true;
            return this;
        }

        /// <summary>
        /// Sets whether the generated type is abstract.
        /// </summary>
        /// <param name="isAbstract">The is abstract value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder Abstract(bool isAbstract = true)
        {
            IsAbstract = isAbstract;
            return this;
        }

        /// <summary>
        /// Adds nested class.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddNestedClass(string name, Accessibility? accessModifier = null) =>
            AddNestedClass(name, false, accessModifier);

        /// <summary>
        /// Adds nested class.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="partial">The partial value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder AddNestedClass(string name, bool partial, Accessibility? accessModifier = null)
        {
            var builder = new ClassBuilder(name, Builder, partial);
            if (accessModifier.HasValue)
                builder.WithAccessModifier(accessModifier.Value);

            _nestedClass.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Adds nested delegate.
        /// </summary>
        /// <param name="name">The name value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public DelegateBuilder AddNestedDelegate(string name, Accessibility? accessModifier = null)
        {
            var builder = new DelegateBuilder(name, Builder);
            if (accessModifier.HasValue)
                builder.WithAccessModifier(accessModifier.Value);

            _nestedDelegates.Enqueue(builder);
            return builder;
        }

        /// <summary>
        /// Builds the current code output and returns the generated source text.
        /// </summary>
        /// <returns>The generated result.</returns>
        public string Build() => Builder.Build();

        /// <summary>
        /// Disables property-name sorting and preserves insertion order.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder DontSortPropertiesByName()
        {
            _propertiesOrderBy = _ => "";
            return this;
        }

        internal override void Write(in CodeWriter writer)
        {
            _xmlDoc?.Write(writer);

            WriteClassAttributes(_classAttributes, writer);

            var staticDeclaration = IsStatic ? "static " : string.Empty;

            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(BaseClass))
            {
                queue.Enqueue(BaseClass!);
            }

            foreach (var inter in _interfaces.Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x))
            {
                queue.Enqueue(inter);
            }

            var extra = queue.Any() ? $": {string.Join(", ", queue)}" : string.Empty;

            foreach (var attr in _attributes)
            {
                writer.AppendLine($"[{attr}]");
            }

            if (_methods.Any(x => x.IsAbstract))
                IsAbstract = true;

            var classDeclaration = new[] {
                AccessModifier.Code(),
                IsStatic ? "static" : null,
                IsSealed ? "sealed" : null,
                IsAbstract ? "abstract" : null,
                _isPartial ? "partial" : null,
                Kind.ToString().ToLowerInvariant(),
                $"{Name}{_generics}",
                extra
            };

            using (writer.Block(string.Join(" ", classDeclaration.Where(x => !string.IsNullOrEmpty(x))), _generics.Contraints()))
            {
                var hadOutput = false;
                hadOutput = InvokeBuilderWrite(_nestedDelegates, ref hadOutput, in writer);
                hadOutput = InvokeBuilderWrite(_events, ref hadOutput, writer);
                var orderBy = _propertiesOrderBy ?? (x => x.Name);
                hadOutput = InvokeBuilderWrite(
                    _properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Const && x.IsStatic == false)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(
                    _properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Const && x.IsStatic == true)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(
                    _properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.ReadOnly)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(
                    _properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Default)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer,
                    true);
                hadOutput = InvokeBuilderWrite(_constructors.OrderBy(x => x.Parameters.Count), ref hadOutput, writer);
                hadOutput = InvokeBuilderWrite(
                    _properties.Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Property)
                        .OrderBy(orderBy),
                    ref hadOutput,
                    writer);
                hadOutput = InvokeBuilderWrite(
                    _methods.OrderBy(x => x.Name)
                        .ThenBy(x => x.Parameters.Count),
                    ref hadOutput,
                    writer);
                InvokeBuilderWrite(_nestedClass, ref hadOutput, writer);
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
                return hadOutput;

            if (hadOutput)
                writer.NewLine();

            var queue = new Queue<T>();
            foreach (var builder in builders)
                queue.Enqueue(builder);

            while (queue.Any())
            {
                var builder = queue.Dequeue();
                builder.Write(writer);

                if (!group && queue.Any())
                    writer.NewLine();
            }

            return true;
        }
    }
}
