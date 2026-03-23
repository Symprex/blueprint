namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Implemented by all builder types so they can be queued on a <see cref="CodeBuilder"/> and rendered in order.
    /// </summary>
    internal interface IBuilder
    {
        /// <summary>
        /// Writes the generated source for this builder to the supplied code writer.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        void Write(in CodeWriter writer);
    }
}
