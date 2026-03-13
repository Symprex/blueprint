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
        protected readonly List<string> _pragmaWarnings = new List<string>();

        internal abstract void Write(in CodeWriter writer);

        void IBuilder.Write(in CodeWriter writer)
        {
            var warnings = string.Join(", ", this._pragmaWarnings.Distinct());
            if (this._pragmaWarnings.Any())
            {
                writer.AppendUnindentedLine($"#pragma warning disable {warnings}");
            }

            this.Write(writer);

            if (this._pragmaWarnings.Any())
            {
                writer.AppendUnindentedLine($"#pragma warning restore {warnings}");
            }
        }
    }
}
