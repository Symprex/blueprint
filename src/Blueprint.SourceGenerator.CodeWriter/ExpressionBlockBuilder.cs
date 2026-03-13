namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Helps populate loop-style blocks such as for, while, and foreach statements against an ICodeWriter.
    /// </summary>
    public sealed class ExpressionBlockBuilder
    {
        private ICodeWriter _writer { get; }
        private string _blockCode { get; }

        internal ExpressionBlockBuilder(ICodeWriter writer, string block)
        {
            this._writer = writer;
            this._blockCode = block;
        }

        /// <summary>
        /// Writes the body of the generated control-flow block and then returns the underlying writer.
        /// </summary>
        /// <param name="innerWrite">A callback that writes the statements inside the generated block.</param>
        /// <returns>The underlying writer so additional source can be emitted.</returns>
        public ICodeWriter WithBody(Action<ICodeWriter> innerWrite)
        {
            using (this._writer.Block(this._blockCode))
            {
                innerWrite?.Invoke(this._writer);
            }

            return this._writer;
        }
    }
}
