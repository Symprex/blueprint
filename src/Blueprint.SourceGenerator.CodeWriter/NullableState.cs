namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Controls which nullable directive is emitted near the top of the generated file.
    /// </summary>
    public enum NullableState
    {
        /// <summary>
        /// Leaves nullable directives unchanged in the generated file.
        /// </summary>
        Default,

        /// <summary>
        /// Emits a directive that enables nullable reference type analysis.
        /// </summary>
        Enable,

        /// <summary>
        /// Emits a directive that disables nullable reference type analysis.
        /// </summary>
        Disable,

        /// <summary>
        /// Emits a directive that restores the project-level nullable setting.
        /// </summary>
        Restore
    }
}
