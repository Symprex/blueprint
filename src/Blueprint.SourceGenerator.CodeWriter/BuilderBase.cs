#pragma warning disable IDE0008
#pragma warning disable IDE0090
#nullable enable
namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Provides the rendering infrastructure shared by builders that need pragma warning suppression around emitted source.
    /// </summary>
    public abstract class BuilderBase : IBuilder
    {
        /// <summary>
        /// Holds the list of compiler warning codes that will be suppressed around this builder's emitted output.
        /// </summary>
        protected readonly List<string> _pragmaWarnings = new List<string>();

        /// <summary>
        /// Emits the source for this builder to the supplied writer without pragma wrappers.
        /// </summary>
        /// <param name="writer">The writer that receives the generated output.</param>
        internal abstract void Write(in CodeWriter writer);

        /// <inheritdoc/>
        void IBuilder.Write(in CodeWriter writer)
        {
            if (this._pragmaWarnings.Count > 0)
            {
                var warnings = string.Join(", ", this._pragmaWarnings.Distinct());
                writer.AppendUnindentedLine($"#pragma warning disable {warnings}");
                this.Write(writer);
                writer.AppendUnindentedLine($"#pragma warning restore {warnings}");
            }
            else
            {
                this.Write(writer);
            }
        }
    }
}
