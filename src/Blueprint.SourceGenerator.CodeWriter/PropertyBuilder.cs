using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0008
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a generated property or field declaration.
    /// Use the value and accessor helpers to decide whether the member is emitted as an auto-property, a backed property, a readonly field, or a const field.
    /// </summary>
    public class PropertyBuilder : IBuilder
    {
        /// <summary>
        /// Distinguishes the different member shapes that <see cref="PropertyBuilder"/> can emit.
        /// </summary>
        internal enum FieldType
        {
            /// <summary>Emits a <c>const</c> field.</summary>
            Const,

            /// <summary>Emits a <c>readonly</c> field.</summary>
            ReadOnly,

            /// <summary>Emits a regular mutable field.</summary>
            Default,

            /// <summary>Emits a property with optional accessors.</summary>
            Property
        }

        private bool _autoprops;

        /// <summary>Controls which kind of member (const, readonly, field, or property) this builder emits.</summary>
        internal FieldType FieldTypeValue = FieldType.Property;

        /// <summary>Controls how a default or null value is emitted for the generated member.</summary>
        internal ValueType PropertyValueType = ValueType.UserSpecified;
        private Action<ICodeWriter>? _getter;
        private string? _getterExpression;
        private Action<ICodeWriter>? _setter;
        private string? _setterExpression;
        private string? _value;
        private bool _getOnly;
        private bool _virtual;
        private bool _override;
        private Accessibility? _setterAccessibility;
        private readonly List<string> _attributes = new List<string>();
        private DocumentationComment? _xmlDoc;

        /// <summary>
        /// Initializes a new instance attached to the supplied class builder.
        /// </summary>
        /// <param name="name">The member name to emit.</param>
        /// <param name="accessModifier">The accessibility modifier to emit, or <see langword="null"/> to omit one.</param>
        /// <param name="builder">The class builder that owns this member.</param>
        internal PropertyBuilder(string name, Accessibility? accessModifier, ClassBuilder builder)
        {
            this.Name = name;
            this.AccessModifier = accessModifier;
            this.Class = builder;
        }

        /// <summary>
        /// Gets the member name that will be emitted for the generated property or field.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the type text that will be emitted for the generated property or field.
        /// </summary>
        public string? Type { get; private set; }

        /// <summary>
        /// Gets the ClassBuilder that owns this generated property or field.
        /// </summary>
        public ClassBuilder Class { get; }

        /// <summary>
        /// Gets the accessibility that will be emitted for the generated property or field.
        /// </summary>
        public Accessibility? AccessModifier { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the generated property or field will be emitted with the static modifier.
        /// </summary>
        public bool IsStatic { get; private set; }

        /// <summary>
        /// Sets the XML summary that will be emitted for the generated property or field.
        /// </summary>
        /// <param name="summary">The caller-facing description of what the generated member represents.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithSummary(string summary)
        {
            this._xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures the generated property or field to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated property. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithInheritDoc(bool inherit = true)
        {
            this._xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithInheritDoc(string from)
        {
            this._xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this property or field.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddNamespaceImport(string importedNamespace)
        {
            this.Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this property or field.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddNamespaceImport(ISymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ContainingNamespace.ToString());
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this property or field.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <param name="type">The C# type text to emit for the generated property or field.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType(string type)
        {
            this.Type = type;
            return this;
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType(INamedTypeSymbol symbol)
        {
            return this.AddNamespaceImport(symbol.ContainingNamespace)
                .SetType(symbol.GetTypeName());
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <param name="type">The C# type text to emit for the generated property or field.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType(Type type)
        {
            return this.AddNamespaceImport(type.Namespace)
                .SetType(type.GetTypeName());
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <typeparam name="T">The CLR type whose name and namespace should be emitted.</typeparam>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType<T>()
        {
            return this.SetType(typeof(T));
        }

        /// <summary>
        /// Sets the generated property or field to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakePublicProperty()
        {
            return this.WithAccessModifier(Accessibility.Public);
        }

        /// <summary>
        /// Sets the generated property or field to private accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakePrivateProperty()
        {
            return this.WithAccessModifier(Accessibility.Private);
        }

        /// <summary>
        /// Sets the generated property or field to protected accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeProtectedProperty()
        {
            return this.WithAccessModifier(Accessibility.Protected);
        }

        /// <summary>
        /// Sets the generated property or field to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeInternalProperty()
        {
            return this.WithAccessModifier(Accessibility.Internal);
        }

        /// <summary>
        /// Sets the accessibility emitted for the generated property or field.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithAccessModifier(Accessibility accessModifier)
        {
            this.AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Sets whether the generated property should be emitted with the override modifier.
        /// </summary>
        /// <param name="override">True to emit the generated declaration with the override modifier; otherwise false.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder Override(bool @override = true)
        {
            this._override = @override;
            return this;
        }

        /// <summary>
        /// Marks the generated property or field as static.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeStatic()
        {
            this.IsStatic = true;
            if (string.IsNullOrEmpty(this._getterExpression))
            {
                this.FieldTypeValue = FieldType.Const;
            }

            return this;
        }

        /// <summary>
        /// Marks the generated property as virtual.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeVirtualProperty()
        {
            this._virtual = true;
            return this;
        }

        /// <summary>
        /// Adds an attribute to the generated property or field if it is not already present.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!this._attributes.Contains(sanitized))
            {
                this._attributes.Add(sanitized);
            }

            return this;
        }

        /// <summary>
        /// Switches the member to a get-only auto-property and returns the parent class builder.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder UseGetOnlyAutoProp()
        {
            this._autoprops = true;
            this._getOnly = true;
            return this.Class;
        }

        /// <summary>
        /// Switches the member to an auto-property and returns the parent class builder.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder UseAutoProps()
        {
            return this.UseAutoProps(null);
        }

        /// <summary>
        /// Switches the member to an auto-property and returns the parent class builder.
        /// </summary>
        /// <param name="setterAccessibility">The setter accessibility used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder UseAutoProps(Accessibility? setterAccessibility)
        {
            this._autoprops = true;
            this._setterAccessibility = setterAccessibility;
            return this.Class;
        }

        /// <summary>
        /// Sets an expression-bodied getter for the generated property.
        /// </summary>
        /// <param name="expression">The header expression to emit before the generated block body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithGetterExpression(string expression)
        {
            this._getterExpression = expression;
            this.FieldTypeValue = FieldType.Property;
            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated getter body.
        /// </summary>
        /// <param name="getterBody">The getter body used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithGetter(Action<ICodeWriter> getterBody)
        {
            this._getter = getterBody;
            return this;
        }

        /// <summary>
        /// Sets an expression-bodied setter for the generated property.
        /// </summary>
        /// <param name="expression">The header expression to emit before the generated block body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithSetterExpression(string expression)
        {
            this._setterExpression = expression;
            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated setter body.
        /// </summary>
        /// <param name="setterBody">The setter body used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithSetter(Action<ICodeWriter> setterBody)
        {
            this._setter = setterBody;
            return this;
        }

        /// <summary>
        /// Converts the member into a constant-style field and assigns the supplied value.
        /// </summary>
        /// <param name="value">The literal value text to emit for the generated field.</param>
        /// <param name="valueType">Controls how the supplied value should be interpreted when the field is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithConstValue(string value, ValueType valueType = ValueType.UserSpecified)
        {
            this._value = value;
            this.FieldTypeValue = FieldType.Const;
            this.PropertyValueType = valueType;
            return this.Class;
        }

        /// <summary>
        /// Configures the member to emit a readonly field with the supplied initializer.
        /// </summary>
        /// <param name="valueType">Controls how the generated readonly value should be interpreted when the field is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithReadonlyValue(ValueType valueType = ValueType.UserSpecified)
        {
            return this.WithReadonlyValue(null, valueType);
        }

        /// <summary>
        /// Configures the member to emit a readonly field with the supplied initializer.
        /// </summary>
        /// <param name="value">The initializer text to emit for the readonly field, or <see langword="null"/> to omit it.</param>
        /// <param name="valueType">Controls how the supplied value should be interpreted when the field is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithReadonlyValue(string? value, ValueType valueType = ValueType.UserSpecified)
        {
            this._value = value;
            this.FieldTypeValue = FieldType.ReadOnly;
            this.PropertyValueType = valueType;
            return this.Class;
        }

        /// <summary>
        /// Configures the member to emit the supplied value when rendered.
        /// </summary>
        /// <param name="value">The value expression to emit for the generated backing field or property initializer.</param>
        /// <param name="valueType">Controls how the supplied value should be interpreted when the member is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithValue(string? value, ValueType valueType = ValueType.UserSpecified)
        {
            this._value = value;
            this.FieldTypeValue = FieldType.Default;
            this.PropertyValueType = valueType;
            return this.Class;
        }

        /// <summary>
        /// Converts the current member into a backing field declaration and optionally assigns an initializer.
        /// </summary>
        /// <param name="defaultValue">The initializer to emit for the generated backing field, or <see langword="null"/> to omit one.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder MakeBackingField(string? defaultValue = null)
        {
            return this.WithValue(defaultValue);
        }

        /// <summary>
        /// Creates a companion backing field for the current property and returns the field builder for additional customization.
        /// </summary>
        /// <param name="defaultValue">The initializer to emit for the backing field, or <see langword="null"/> to omit one.</param>
        /// <param name="accessModifier">The accessibility to use for the generated backing field.</param>
        /// <returns>The builder for the generated backing field.</returns>
        public PropertyBuilder WithBackingField(string? defaultValue = null, Accessibility? accessModifier = null)
        {
            var name = $"_{char.ToLower(this.Name[0])}{this.Name.Substring(1)}";
            var builder = this.Class.AddProperty(name, accessModifier ?? Accessibility.Private);
            builder.Type = this.Type;
            builder.FieldTypeValue = FieldType.Default;
            builder.WithValue(defaultValue);

            return this;
        }

        /// <inheritdoc/>
        void IBuilder.Write(in CodeWriter writer)
        {
            this._xmlDoc?.Write(writer);

            foreach (var attribute in this._attributes)
            {
                writer.AppendLine($"[{attribute}]");
            }

            var value = this.PropertyValueType switch
            {
                ValueType.Null => "null",
                ValueType.Default => "default",
                _ => this._value
            };

            if (this.Type is null || string.IsNullOrEmpty(this.Type))
            {
                throw new ArgumentNullException($"There is no 'Type' Specified for {this.Name}");
            }

            var type = this.Type.Trim();
            var name = this.Name.Trim();
            var _static = this.IsStatic ? " static" : null;
            var isNew = name == nameof(Equals) ? " new" : string.Empty;
            string? additionalModifier = null;
            if (this._virtual)
            {
                additionalModifier = "virtual";
            }
            else if (this._override)
            {
                additionalModifier = "override";
            }

            var output = (this.FieldTypeValue switch
            {
                FieldType.Const => $"{this.AccessModifier.Code()}{isNew} const {type} {name}",
                FieldType.ReadOnly => $"{this.AccessModifier.Code()}{isNew}{_static} readonly {type} {name}",
                _ => additionalModifier is null
                    ? $"{this.AccessModifier.Code()}{isNew}{_static} {type} {name}"
                    : $"{this.AccessModifier.Code()} {additionalModifier} {type} {name}"
            }).Trim();

            var maxCharacters = (value?.StartsWith("\"") ?? false) ? 9 : 5;

            if (this.FieldTypeValue != FieldType.Property)
            {
                if (string.IsNullOrEmpty(value) && this.FieldTypeValue != FieldType.Const)
                {
                    writer.AppendLine($"{output};");
                }
                else if (value?.Length > maxCharacters)
                {
                    writer.AppendLine($"{output} =");
                    writer.IncreaseIndent();
                    writer.AppendLine($"{value};");
                    writer.DecreaseIndent();
                }
                else
                {
                    writer.AppendLine($"{output} = {value};");
                }

                return;
            }
            else if (this._autoprops)
            {
                if (this._getOnly)
                {
                    writer.AppendLine($"{output} {{ get; }}");
                    return;
                }

                var set = $"{this._setterAccessibility.Code()} set".Trim();
                writer.AppendLine($"{output} {{ get; {set}; }}");
                return;
            }

            if (!string.IsNullOrEmpty(this._getterExpression) &&
                string.IsNullOrEmpty(this._setterExpression) &&
                this._setter is null)
            {
                writer.AppendLine($"{output} => {this._getterExpression};");
                return;
            }

            if (string.IsNullOrEmpty(this._getterExpression) && string.IsNullOrEmpty(this._setterExpression) &&
                this._getter is null && this._setter is null)
            {
                writer.AppendLine($"{output};");
                return;
            }

            using (writer.Block(output))
            {
                if (!string.IsNullOrEmpty(this._getterExpression))
                {
                    writer.AppendLine($"get => {this._getterExpression};");
                }
                else if (this._getter != null)
                {
                    using (writer.Block("get"))
                    {
                        this._getter.Invoke(writer);
                    }
                }

                if (!string.IsNullOrEmpty(this._setterExpression))
                {
                    writer.AppendLine($"set => {this._setterExpression};");
                }
                else if (this._setter != null)
                {
                    using (writer.Block("set"))
                    {
                        this._setter.Invoke(writer);
                    }
                }
            }
        }
    }
}
