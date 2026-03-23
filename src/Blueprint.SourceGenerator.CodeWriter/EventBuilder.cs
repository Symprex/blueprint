using Blueprint.SourceGenerator.CodeWriter.Internals;
using Microsoft.CodeAnalysis;

#pragma warning disable IDE0008
#pragma warning disable IDE0090
#pragma warning disable IDE1006
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds a generated event declaration, including delegate type, accessibility, and custom add/remove behavior.
    /// </summary>
    public class EventBuilder : BuilderBase<EventBuilder>
    {
        private Accessibility _declaredAccessibility = Accessibility.Public;
        private string? _explicitImplementation = null;
        private string _eventDelegateType = nameof(EventHandler);
        private Action<ICodeWriter>? _addHandlerDelegate = null;
        private Action<ICodeWriter>? _removeHandlerDelegate = null;
        private bool _static = false;

        /// <summary>
        /// Initializes a new instance attached to the supplied class builder.
        /// </summary>
        /// <param name="class">The class builder that owns this event.</param>
        /// <param name="eventName">The event name to emit.</param>
        internal EventBuilder(ClassBuilder @class, string eventName)
        {
            this.Name = eventName;
            this.Class = @class;
        }

        /// <summary>
        /// Gets the event name that will be emitted.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the ClassBuilder that owns this generated event.
        /// </summary>
        public ClassBuilder Class { get; }

        /// <summary>
        /// Sets whether the generated event should be emitted as static.
        /// </summary>
        /// <param name="isStatic">True to emit the generated event as static; otherwise false.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder MakeStatic(bool isStatic = true)
        {
            this._static = isStatic;
            return this;
        }

        /// <summary>
        /// Configures add and remove accessors that delegate to the supplied backing field.
        /// </summary>
        /// <param name="backingField">The backing field expression that the generated event accessors should use.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder WithBackingField(string backingField)
        {
            return this.WithAddExpression($"{backingField} += value")
                .WithRemoveExpression($"{backingField} -= value")
                .Class;
        }

        /// <summary>
        /// Supplies the callback that writes the generated add accessor body.
        /// </summary>
        /// <param name="addDelegate">A callback that writes the generated add accessor body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithAddHandler(Action<ICodeWriter> addDelegate)
        {
            void BlockHandler(ICodeWriter writer)
            {
                using (writer.Block("add"))
                {
                    addDelegate?.Invoke(writer);
                }
            }

            this._addHandlerDelegate = BlockHandler;
            return this;
        }

        /// <summary>
        /// Sets an expression-bodied add accessor for the generated event.
        /// </summary>
        /// <param name="addDelegateExpression">The expression to emit for the generated accessor.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithAddExpression(string addDelegateExpression)
        {
            this._addHandlerDelegate = w => w.AppendLine($"add => {addDelegateExpression};");
            return this;
        }

        /// <summary>
        /// Supplies the callback that writes the generated remove accessor body.
        /// </summary>
        /// <param name="addDelegate">A callback that writes the generated add accessor body.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithRemoveHandler(Action<ICodeWriter> addDelegate)
        {
            void BlockHandler(ICodeWriter writer)
            {
                using (writer.Block("remove"))
                {
                    addDelegate?.Invoke(writer);
                }
            }

            this._removeHandlerDelegate = BlockHandler;
            return this;
        }

        /// <summary>
        /// Sets an expression-bodied remove accessor for the generated event.
        /// </summary>
        /// <param name="addDelegateExpression">The expression to emit for the generated accessor.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithRemoveExpression(string addDelegateExpression)
        {
            this._removeHandlerDelegate = w => w.AppendLine($"remove => {addDelegateExpression};");
            return this;
        }

        /// <summary>
        /// Configures the generated event to be emitted as an explicit interface implementation.
        /// </summary>
        /// <param name="inferfaceName">The interface name to use for explicit event implementation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithExplicitImplementation(string @inferfaceName)
        {
            this._explicitImplementation = inferfaceName;
            return this;
        }

        /// <summary>
        /// Builds the common explicit-interface event pattern, including a backing field event.
        /// </summary>
        /// <param name="interfaceName">The interface name to use for explicit event implementation.</param>
        /// <param name="backingFieldAccessibility">The accessibility to use for the generated backing event field.</param>
        /// <param name="backingFieldName">The field name to use for the generated backing event field.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public ClassBuilder WithDefaultExplicitImplementation(string @interfaceName, Accessibility backingFieldAccessibility = Accessibility.Private, string? backingFieldName = null)
        {
            if (backingFieldName is null || string.IsNullOrEmpty(backingFieldName))
            {
                backingFieldName = $"_{char.ToLower(this.Name[0])}{this.Name.Substring(1)}";
            }

            return this.WithExplicitImplementation(interfaceName)
                .WithBackingField(backingFieldName)
                .AddEvent(backingFieldName)
                .WithAccessibility(backingFieldAccessibility)
                .WithDelegateHandler(this._eventDelegateType)
                .Class;
        }

        /// <summary>
        /// Sets the delegate type emitted for the generated event.
        /// </summary>
        /// <param name="handlerType">The event delegate type to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithDelegateHandler(string handlerType)
        {
            this._eventDelegateType = handlerType;
            return this;
        }

        /// <summary>
        /// Sets the delegate type emitted for the generated event.
        /// </summary>
        /// <param name="handlerType">The event delegate type to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithDelegateHandler(INamedTypeSymbol handlerType)
        {
            this._eventDelegateType = handlerType.Name;
            return this.AddNamespaceImport(handlerType);
        }

        /// <summary>
        /// Sets the accessibility emitted for the generated event.
        /// </summary>
        /// <param name="accessibility">The accessibility modifier to emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder WithAccessibility(Accessibility accessibility)
        {
            this._declaredAccessibility = accessibility;
            return this;
        }

        /// <summary>
        /// Sets the generated event to public accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder MakePublic()
        {
            this._declaredAccessibility = Accessibility.Public;
            return this;
        }

        /// <summary>
        /// Sets the generated event to protected accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder MakeProtected()
        {
            this._declaredAccessibility = Accessibility.Protected;
            return this;
        }

        /// <summary>
        /// Sets the generated event to private accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder MakePrivate()
        {
            this._declaredAccessibility = Accessibility.Private;
            return this;
        }

        /// <summary>
        /// Sets the generated event to internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder MakeInternal()
        {
            this._declaredAccessibility = Accessibility.Internal;
            return this;
        }

        /// <summary>
        /// Sets the generated event to protected internal accessibility.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public EventBuilder MakeInternalProtected()
        {
            this._declaredAccessibility = Accessibility.ProtectedAndInternal;
            return this;
        }

        /// <summary>
        /// Queues an assembly-level attribute on the generated file that owns this event.
        /// </summary>
        /// <param name="attribute">The attribute to inspect or emit.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override EventBuilder AddAssemblyAttribute(string attribute)
        {
            this.Class.AddAssemblyAttribute(attribute);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this event.
        /// </summary>
        /// <param name="importedNamespace">The namespace to emit as a using directive so referenced types do not need to be fully qualified.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override EventBuilder AddNamespaceImport(string importedNamespace)
        {
            this.Class.AddNamespaceImport(importedNamespace);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this event.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override EventBuilder AddNamespaceImport(ISymbol symbol)
        {
            this.Class.AddNamespaceImport(symbol);
            return this;
        }

        /// <summary>
        /// Registers a namespace import on the generated file that owns this event.
        /// </summary>
        /// <param name="symbol">The Roslyn symbol whose namespace or type information should be mirrored into generated source.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public override EventBuilder AddNamespaceImport(INamespaceSymbol symbol)
        {
            this.Class.AddNamespaceImport(symbol);
            return this;
        }

        /// <inheritdoc/>
        internal override void Write(in CodeWriter writer)
        {
            var @static = this._static ? "static " : string.Empty;
            var eventDeclaration = $"{this._declaredAccessibility.Code()} {@static}event {this._eventDelegateType} {this.Name}";
            if (!string.IsNullOrEmpty(this._explicitImplementation))
            {
                eventDeclaration = $"event {@static}{this._eventDelegateType} {this._explicitImplementation}.{this.Name}";
            }

            if (this._addHandlerDelegate is null || this._removeHandlerDelegate is null)
            {
                writer.AppendLine($"{eventDeclaration};");
                return;
            }

            using (writer.Block(eventDeclaration))
            {
                this._addHandlerDelegate(writer);
                this._removeHandlerDelegate(writer);
            }
        }
    }
}
