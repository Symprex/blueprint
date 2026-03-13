using System;

namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents expression block builder.
    /// </summary>
    public sealed class ExpressionBlockBuilder
    {
        private ICodeWriter _writer { get; }
        private string _blockCode { get; }

        internal ExpressionBlockBuilder(ICodeWriter writer, string block)
        {
            _writer = writer;
            _blockCode = block;
        }

        /// <summary>
        /// Configures body.
        /// </summary>
        /// <param name="innerWrite">The inner write value.</param>
        /// <returns>The generated result.</returns>
        public ICodeWriter WithBody(Action<ICodeWriter> innerWrite)
        {
            using (_writer.Block(_blockCode))
            {
                innerWrite?.Invoke(_writer);
            }

            return _writer;
        }
    }
}
