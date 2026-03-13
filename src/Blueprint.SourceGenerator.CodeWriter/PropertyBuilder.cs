using System;
using System.Collections.Generic;
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
        internal enum FieldType
        {
            Const,
            ReadOnly,
            Default,
            Property
        }

        private bool _autoprops;
        internal FieldType FieldTypeValue = FieldType.Property;
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

        internal PropertyBuilder(string name, Accessibility? accessModifier, ClassBuilder builder)
        {
            Name = name;
            AccessModifier = accessModifier;
            Class = builder;
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
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures the generated property or field to emit inheritdoc XML instead of explicit text.
        /// </summary>
        /// <param name="inherit">Set to <see langword="true"/> to emit <c>&lt;inheritdoc/&gt;</c> for the generated property. The parameter keeps the fluent API readable in conditional code paths.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures the generated declaration to emit inheritdoc XML targeting a specific cref.
        /// </summary>
        /// <param name="from">The member or type to reference from the emitted inheritdoc cref.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this property or field.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddNamespaceImport(string importedNamespace)
        {
            Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this property or field.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddNamespaceImport(ISymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace.ToString());
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this property or field.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <param name="type">The C# type text to emit for the generated property or field.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType(string type)
        {
            Type = type;
            return this;
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType(INamedTypeSymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace)
                .SetType(symbol.GetTypeName());
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <param name="type">The C# type text to emit for the generated property or field.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType(Type type)
        {
            return AddNamespaceImport(type.Namespace)
                .SetType(type.GetTypeName());
        }

        /// <summary>
        /// Sets the type emitted for the generated property or field.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder SetType<T>() => SetType(typeof(T));

        /// <summary>
        /// Sets the generated property or field to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakePublicProperty() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Sets the generated property or field to private accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakePrivateProperty() => WithAccessModifier(Accessibility.Private);

        /// <summary>
        /// Sets the generated property or field to protected accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeProtectedProperty() => WithAccessModifier(Accessibility.Protected);

        /// <summary>
        /// Sets the generated property or field to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeInternalProperty() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Sets the accessibility emitted for the generated property or field.
        /// </summary>
        /// <param name="accessModifier">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Sets whether the generated property should be emitted with the override modifier.
        /// </summary>
        /// <param name="override">True to emit the generated declaration with the override modifier; otherwise false.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder Override(bool @override = true)
        {
            _override = @override;
            return this;
        }

        /// <summary>
        /// Marks the generated property or field as static.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeStatic()
        {
            IsStatic = true;
            if (string.IsNullOrEmpty(_getterExpression))
            {
                FieldTypeValue = FieldType.Const;
            }

            return this;
        }

        /// <summary>
        /// Marks the generated property as virtual.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder MakeVirtualProperty()
        {
            _virtual = true;
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
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        /// <summary>
        /// Switches the member to a get-only auto-property and returns the parent class builder.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder UseGetOnlyAutoProp()
        {
            _autoprops = true;
            _getOnly = true;
            return Class;
        }

        /// <summary>
        /// Switches the member to an auto-property and returns the parent class builder.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder UseAutoProps() =>
            UseAutoProps(null);

        /// <summary>
        /// Switches the member to an auto-property and returns the parent class builder.
        /// </summary>
        /// <param name="setterAccessibility">The setter accessibility used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder UseAutoProps(Accessibility? setterAccessibility)
        {
            _autoprops = true;
            _setterAccessibility = setterAccessibility;
            return Class;
        }

        /// <summary>
        /// Sets an expression-bodied getter for the generated property.
        /// </summary>
        /// <param name="expression">The header expression to emit before the generated block body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithGetterExpression(string expression)
        {
            _getterExpression = expression;
            FieldTypeValue = FieldType.Property;
            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated getter body.
        /// </summary>
        /// <param name="getterBody">The getter body used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithGetter(Action<ICodeWriter> getterBody)
        {
            _getter = getterBody;
            return this;
        }

        /// <summary>
        /// Sets an expression-bodied setter for the generated property.
        /// </summary>
        /// <param name="expression">The header expression to emit before the generated block body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithSetterExpression(string expression)
        {
            _setterExpression = expression;
            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated setter body.
        /// </summary>
        /// <param name="setterBody">The setter body used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public PropertyBuilder WithSetter(Action<ICodeWriter> setterBody)
        {
            _setter = setterBody;
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
            _value = value;
            FieldTypeValue = FieldType.Const;
            PropertyValueType = valueType;
            return Class;
        }

        /// <summary>
        /// Configures the member to emit a readonly field with the supplied initializer.
        /// </summary>
        /// <param name="valueType">Controls how the generated readonly value should be interpreted when the field is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithReadonlyValue(ValueType valueType = ValueType.UserSpecified) =>
            WithReadonlyValue(null, valueType);

        /// <summary>
        /// Configures the member to emit a readonly field with the supplied initializer.
        /// </summary>
        /// <param name="value">The initializer text to emit for the readonly field, or <see langword="null"/> to omit it.</param>
        /// <param name="valueType">Controls how the supplied value should be interpreted when the field is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithReadonlyValue(string? value, ValueType valueType = ValueType.UserSpecified)
        {
            _value = value;
            FieldTypeValue = FieldType.ReadOnly;
            PropertyValueType = valueType;
            return Class;
        }

        /// <summary>
        /// Configures the member to emit the supplied value when rendered.
        /// </summary>
        /// <param name="value">The value expression to emit for the generated backing field or property initializer.</param>
        /// <param name="valueType">Controls how the supplied value should be interpreted when the member is rendered.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder WithValue(string? value, ValueType valueType = ValueType.UserSpecified)
        {
            _value = value;
            FieldTypeValue = FieldType.Default;
            PropertyValueType = valueType;
            return Class;
        }

        /// <summary>
        /// Converts the current member into a backing field declaration and optionally assigns an initializer.
        /// </summary>
        /// <param name="defaultValue">The initializer to emit for the generated backing field, or <see langword="null"/> to omit one.</param>
        /// <returns>The parent class builder so type construction can continue.</returns>
        public ClassBuilder MakeBackingField(string? defaultValue = null) =>
            WithValue(defaultValue);

        /// <summary>
        /// Creates a companion backing field for the current property and returns the field builder for additional customization.
        /// </summary>
        /// <param name="defaultValue">The initializer to emit for the backing field, or <see langword="null"/> to omit one.</param>
        /// <param name="accessModifier">The accessibility to use for the generated backing field.</param>
        /// <returns>The builder for the generated backing field.</returns>
        public PropertyBuilder WithBackingField(string? defaultValue = null, Accessibility? accessModifier = null)
        {
            var name = $"_{char.ToLower(Name[0])}{Name.Substring(1)}";
            var builder = Class.AddProperty(name, accessModifier ?? Accessibility.Private);
            builder.Type = Type;
            builder.FieldTypeValue = FieldType.Default;
            builder.WithValue(defaultValue);

            return this;
        }

        void IBuilder.Write(in CodeWriter writer)
        {
            _xmlDoc?.Write(writer);

            foreach (var attribute in _attributes)
                writer.AppendLine($"[{attribute}]");

            var value = PropertyValueType switch
            {
                ValueType.Null => "null",
                ValueType.Default => "default",
                _ => _value
            };

            if (Type is null || string.IsNullOrEmpty(Type))
                throw new ArgumentNullException($"There is no 'Type' Specified for {Name}");

            var type = Type.Trim();
            var name = Name.Trim();
            var _static = IsStatic ? " static" : null;
            var isNew = name == nameof(Equals) ? " new" : string.Empty;
            string? additionalModifier = null;
            if (_virtual)
                additionalModifier = "virtual";
            else if (_override)
                additionalModifier = "override";

            var output = (FieldTypeValue switch
            {
                FieldType.Const => $"{AccessModifier.Code()}{isNew} const {type} {name}",
                FieldType.ReadOnly => $"{AccessModifier.Code()}{isNew}{_static} readonly {type} {name}",
                _ => additionalModifier is null
                    ? $"{AccessModifier.Code()}{isNew}{_static} {type} {name}"
                    : $"{AccessModifier.Code()} {additionalModifier} {type} {name}"
            }).Trim();

            var maxCharacters = (value?.StartsWith("\"") ?? false) ? 9 : 5;

            if(FieldTypeValue != FieldType.Property)
            {
                if(string.IsNullOrEmpty(value) && FieldTypeValue != FieldType.Const)
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
            else if(_autoprops)
            {
                if (_getOnly)
                {
                    writer.AppendLine($"{output} {{ get; }}");
                    return;
                }

                var set = $"{_setterAccessibility.Code()} set".Trim();
                writer.AppendLine($"{output} {{ get; {set}; }}");
                return;
            }

            if(!string.IsNullOrEmpty(_getterExpression) &&
                string.IsNullOrEmpty(_setterExpression) &&
                _setter is null)
            {
                writer.AppendLine($"{output} => {_getterExpression};");
                return;
            }

            if(string.IsNullOrEmpty(_getterExpression) && string.IsNullOrEmpty(_setterExpression) &&
                _getter is null && _setter is null)
            {
                writer.AppendLine($"{output};");
                return;
            }

            using (writer.Block(output))
            {
                if(!string.IsNullOrEmpty(_getterExpression))
                {
                    writer.AppendLine($"get => {_getterExpression};");
                }
                else if(_getter != null)
                {
                    using (writer.Block("get"))
                    {
                        _getter.Invoke(writer);
                    }
                }

                if (!string.IsNullOrEmpty(_setterExpression))
                {
                    writer.AppendLine($"set => {_setterExpression};");
                }
                else if (_setter != null)
                {
                    using (writer.Block("set"))
                    {
                        _setter.Invoke(writer);
                    }
                }
            }
        }
    }
}
