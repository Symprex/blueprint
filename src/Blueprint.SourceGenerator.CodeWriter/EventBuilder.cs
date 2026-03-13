using System;
using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0008
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents event builder.
    /// </summary>
    public class EventBuilder : BuilderBase<EventBuilder>
    {
        private Accessibility _declaredAccessibility = Accessibility.Public;
        private string? _explicitImplementation = null;
        private string _eventDelegateType = nameof(EventHandler);
        private Action<ICodeWriter>? _addHandlerDelegate = null;
        private Action<ICodeWriter>? _removeHandlerDelegate = null;
        private bool _static = false;

        internal EventBuilder(ClassBuilder @class, string eventName)
        {
            Name = eventName;
            Class = @class;
        }

        public string Name { get; }

        public ClassBuilder Class { get; }

        /// <summary>
        /// Marks static.
        /// </summary>
        /// <param name="isStatic">The is static value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder MakeStatic(bool isStatic = true)
        {
            _static = isStatic;
            return this;
        }

        /// <summary>
        /// Configures backing field.
        /// </summary>
        /// <param name="backingField">The backing field value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithBackingField(string backingField)
        {
            return WithAddExpression($"{backingField} += value")
                .WithRemoveExpression($"{backingField} -= value")
                .Class;
        }

        /// <summary>
        /// Configures add handler.
        /// </summary>
        /// <param name="addDelegate">The add delegate value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithAddHandler(Action<ICodeWriter> addDelegate)
        {
            void BlockHandler(ICodeWriter writer)
            {
                using (writer.Block("add"))
                    addDelegate?.Invoke(writer);
            }

            _addHandlerDelegate = BlockHandler;
            return this;
        }

        /// <summary>
        /// Configures add expression.
        /// </summary>
        /// <param name="addDelegateExpression">The add delegate expression value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithAddExpression(string addDelegateExpression)
        {
            _addHandlerDelegate = w => w.AppendLine($"add => {addDelegateExpression};");
            return this;
        }

        /// <summary>
        /// Configures remove handler.
        /// </summary>
        /// <param name="addDelegate">The add delegate value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithRemoveHandler(Action<ICodeWriter> addDelegate)
        {
            void BlockHandler(ICodeWriter writer)
            {
                using (writer.Block("remove"))
                    addDelegate?.Invoke(writer);
            }

            _removeHandlerDelegate = BlockHandler;
            return this;
        }

        /// <summary>
        /// Configures remove expression.
        /// </summary>
        /// <param name="addDelegateExpression">The add delegate expression value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithRemoveExpression(string addDelegateExpression)
        {
            _removeHandlerDelegate = w => w.AppendLine($"remove => {addDelegateExpression};");
            return this;
        }

        /// <summary>
        /// Configures explicit implementation.
        /// </summary>
        /// <param name="inferfaceName">The inferface name value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithExplicitImplementation(string @inferfaceName)
        {
            _explicitImplementation = inferfaceName;
            return this;
        }

        /// <summary>
        /// Configures default explicit implementation.
        /// </summary>
        /// <param name="interfaceName">The interface name value.</param>
        /// <param name="backingFieldAccessibility">The backing field accessibility value.</param>
        /// <param name="backingFieldName">The backing field name value.</param>
        /// <returns>The generated result.</returns>
        public ClassBuilder WithDefaultExplicitImplementation(string @interfaceName, Accessibility backingFieldAccessibility = Accessibility.Private, string? backingFieldName = null)
        {
            if (backingFieldName is null || string.IsNullOrEmpty(backingFieldName))
                backingFieldName = $"_{char.ToLower(Name[0])}{Name.Substring(1)}";

            return WithExplicitImplementation(interfaceName)
                .WithBackingField(backingFieldName)
                .AddEvent(backingFieldName)
                .WithAccessibility(backingFieldAccessibility)
                .WithDelegateHandler(_eventDelegateType)
                .Class;
        }

        /// <summary>
        /// Configures delegate handler.
        /// </summary>
        /// <param name="handlerType">The handler type value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithDelegateHandler(string handlerType)
        {
            _eventDelegateType = handlerType;
            return this;
        }

        /// <summary>
        /// Configures delegate handler.
        /// </summary>
        /// <param name="handlerType">The handler type value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithDelegateHandler(INamedTypeSymbol handlerType)
        {
            _eventDelegateType = handlerType.Name;
            return AddNamespaceImport(handlerType);
        }

        /// <summary>
        /// Configures accessibility.
        /// </summary>
        /// <param name="accessibility">The accessibility value.</param>
        /// <returns>The generated result.</returns>
        public EventBuilder WithAccessibility(Accessibility accessibility)
        {
            _declaredAccessibility = accessibility;
            return this;
        }

        /// <summary>
        /// Marks public.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EventBuilder MakePublic()
        {
            _declaredAccessibility = Accessibility.Public;
            return this;
        }

        /// <summary>
        /// Marks protected.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EventBuilder MakeProtected()
        {
            _declaredAccessibility = Accessibility.Protected;
            return this;
        }

        /// <summary>
        /// Marks private.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EventBuilder MakePrivate()
        {
            _declaredAccessibility = Accessibility.Private;
            return this;
        }

        /// <summary>
        /// Marks internal.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EventBuilder MakeInternal()
        {
            _declaredAccessibility = Accessibility.Internal;
            return this;
        }

        /// <summary>
        /// Marks internal protected.
        /// </summary>
        /// <returns>The generated result.</returns>
        public EventBuilder MakeInternalProtected()
        {
            _declaredAccessibility = Accessibility.ProtectedAndInternal;
            return this;
        }

        /// <summary>
        /// Adds assembly attribute.
        /// </summary>
        /// <param name="attribute">The attribute value.</param>
        /// <returns>The generated result.</returns>
        public override EventBuilder AddAssemblyAttribute(string attribute)
        {
            Class.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="importedNamespace">The imported namespace value.</param>
        /// <returns>The generated result.</returns>
        public override EventBuilder AddNamespaceImport(string importedNamespace)
        {
            Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override EventBuilder AddNamespaceImport(ISymbol symbol)
        {
            Class.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Adds namespace import.
        /// </summary>
        /// <param name="symbol">The symbol value.</param>
        /// <returns>The generated result.</returns>
        public override EventBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            Class.AddNamespaceImport(symbol);
            return this;
        }

        internal override void Write(in CodeWriter writer)
        {
            var @static = _static ? "static " : string.Empty;
            var eventDeclaration = $"{_declaredAccessibility.Code()} {@static}event {_eventDelegateType} {Name}";
            if(!string.IsNullOrEmpty(_explicitImplementation))
            {
                eventDeclaration = $"event {@static}{_eventDelegateType} {_explicitImplementation}.{Name}";
            }

            if(_addHandlerDelegate is null || _removeHandlerDelegate is null)
            {
                writer.AppendLine($"{eventDeclaration};");
                return;
            }

            using (writer.Block(eventDeclaration))
            {
                _addHandlerDelegate(writer);
                _removeHandlerDelegate(writer);
            }
        }
    }
}
