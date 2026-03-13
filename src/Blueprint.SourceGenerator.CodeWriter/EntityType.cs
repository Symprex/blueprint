namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Identifies the high-level declaration kind that a generated source artifact represents.
    /// </summary>
    public enum EntityType
    {
        /// <summary>
        /// Use this when the generated artifact should emit a class declaration.
        /// </summary>
        Class,

        /// <summary>
        /// Use this when the generated artifact should emit a struct declaration.
        /// </summary>
        Struct,

        /// <summary>
        /// Use this when the generated artifact should emit an enum declaration.
        /// </summary>
        Enum
    }
}
