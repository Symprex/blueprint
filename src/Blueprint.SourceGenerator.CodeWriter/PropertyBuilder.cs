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
    /// Builds property and field declarations for generated types.
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

        public string Name { get; }

        public string? Type { get; private set; }

        public ClassBuilder Class { get; }

        public Accessibility? AccessModifier { get; private set; }

        public bool IsStatic { get; private set; }

        /// <summary>
        /// Configures summary.
        /// </summary>
        /// <param name="summary">The summary value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithSummary(string summary)
        {
            _xmlDoc = new SummaryDocumentationComment { Summary = summary };
            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="inherit">The inherit value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithInheritDoc(bool inherit = true)
        {
            _xmlDoc = new InheritDocumentationComment();
            return this;
        }

        /// <summary>
        /// Configures inherit doc.
        /// </summary>
        /// <param name="from">The from value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithInheritDoc(string from)
        {
            _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder AddNamespaceImport(string importedNamespace)
        {
            Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder AddNamespaceImport(ISymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace.ToString());
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            return AddNamespaceImport(symbol.ToString());
        }

        /// <summary>
        /// Sets type.
        /// </summary>
        /// <param name="type">The type value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder SetType(string type)
        {
            Type = type;
            return this;
        }

        /// <summary>
        /// Sets type.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder SetType(INamedTypeSymbol symbol)
        {
            return AddNamespaceImport(symbol.ContainingNamespace)
                .SetType(symbol.GetTypeName());
        }

        /// <summary>
        /// Sets type.
        /// </summary>
        /// <param name="type">The type value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder SetType(Type type)
        {
            return AddNamespaceImport(type.Namespace)
                .SetType(type.GetTypeName());
        }

        /// <summary>
        /// Sets type.
        /// </summary>
        /// <returns>The generated result.</returns>
        public PropertyBuilder SetType<T>() => SetType(typeof(T));

        /// <summary>
        /// Marks public property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public PropertyBuilder MakePublicProperty() => WithAccessModifier(Accessibility.Public);

        /// <summary>
        /// Marks private property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public PropertyBuilder MakePrivateProperty() => WithAccessModifier(Accessibility.Private);

        /// <summary>
        /// Marks protected property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public PropertyBuilder MakeProtectedProperty() => WithAccessModifier(Accessibility.Protected);

        /// <summary>
        /// Marks internal property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public PropertyBuilder MakeInternalProperty() => WithAccessModifier(Accessibility.Internal);

        /// <summary>
        /// Configures access modifier.
        /// </summary>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithAccessModifier(Accessibility accessModifier)
        {
            AccessModifier = accessModifier;
            return this;
        }

        /// <summary>
        /// Sets whether the generated property is emitted with the override modifier.
        /// </summary>
        /// <param name="override">The override value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder Override(bool @override = true)
        {
            _override = @override;
            return this;
        }

        /// <summary>
        /// Marks static.
        /// </summary>
        /// <returns>The generated result.</returns>
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
        /// Marks virtual property.
        /// </summary>
        /// <returns>The generated result.</returns>
        public PropertyBuilder MakeVirtualProperty()
        {
            _virtual = true;
            return this;
        }

        /// <summary>
        /// Adds attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder AddAttribute(string attribute)
        {
            var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
            if (!_attributes.Contains(sanitized))
                _attributes.Add(sanitized);

            return this;
        }

        /// <summary>
        /// Converts this member to a get-only auto-property and returns the parent class builder.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder UseGetOnlyAutoProp()
        {
            _autoprops = true;
            _getOnly = true;
            return Class;
        }

        /// <summary>
        /// Converts this member to an auto-property and returns the parent class builder.
        /// </summary>
        /// <returns>The generated result.</returns>
        public ClassBuilder UseAutoProps() =>
            UseAutoProps(null);

        /// <summary>
        /// Converts this member to an auto-property and returns the parent class builder.
        /// </summary>
        /// <param name="setterAccessibility">The setter accessibility value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder UseAutoProps(Accessibility? setterAccessibility)
        {
            _autoprops = true;
            _setterAccessibility = setterAccessibility;
            return Class;
        }

        /// <summary>
        /// Configures getter expression.
        /// </summary>
        /// <param name="expression">The expression value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithGetterExpression(string expression)
        {
            _getterExpression = expression;
            FieldTypeValue = FieldType.Property;
            return this;
        }

        /// <summary>
        /// Configures getter.
        /// </summary>
        /// <param name="getterBody">The getter body value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithGetter(Action<ICodeWriter> getterBody)
        {
            _getter = getterBody;
            return this;
        }

        /// <summary>
        /// Configures setter expression.
        /// </summary>
        /// <param name="expression">The expression value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithSetterExpression(string expression)
        {
            _setterExpression = expression;
            return this;
        }

        /// <summary>
        /// Configures setter.
        /// </summary>
        /// <param name="setterBody">The setter body value.</param>
        /// <returns>The generated result.</returns>
        public PropertyBuilder WithSetter(Action<ICodeWriter> setterBody)
        {
            _setter = setterBody;
            return this;
        }

        /// <summary>
        /// Configures const value.
        /// </summary>
        /// <param name="value">The value value.</param>
        /// <param name="valueType">The value type value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithConstValue(string value, ValueType valueType = ValueType.UserSpecified)
        {
            _value = value;
            FieldTypeValue = FieldType.Const;
            PropertyValueType = valueType;
            return Class;
        }

        /// <summary>
        /// Configures readonly value.
        /// </summary>
        /// <param name="valueType">The value type value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithReadonlyValue(ValueType valueType = ValueType.UserSpecified) =>
            WithReadonlyValue(null, valueType);

        /// <summary>
        /// Configures readonly value.
        /// </summary>
        /// <param name="value">The value value.</param>
        /// <param name="valueType">The value type value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithReadonlyValue(string? value, ValueType valueType = ValueType.UserSpecified)
        {
            _value = value;
            FieldTypeValue = FieldType.ReadOnly;
            PropertyValueType = valueType;
            return Class;
        }

        /// <summary>
        /// Configures value.
        /// </summary>
        /// <param name="value">The value value.</param>
        /// <param name="valueType">The value type value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithValue(string? value, ValueType valueType = ValueType.UserSpecified)
        {
            _value = value;
            FieldTypeValue = FieldType.Default;
            PropertyValueType = valueType;
            return Class;
        }

        /// <summary>
        /// Marks backing field.
        /// </summary>
        /// <param name="defaultValue">The default value value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder MakeBackingField(string? defaultValue = null) =>
            WithValue(defaultValue);

        /// <summary>
        /// Configures backing field.
        /// </summary>
        /// <param name="defaultValue">The default value value.</param>
        /// <param name="accessModifier">The access modifier value.</param>
        /// <returns>The generated result.</returns>
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
