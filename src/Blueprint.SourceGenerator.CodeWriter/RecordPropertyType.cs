namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Controls whether a generated record emits positional properties or init-only properties.
    /// </summary>
    public enum RecordPropertyType
    {
        /// <summary>
        /// Emits record properties as positional constructor parameters.
        /// </summary>
        Positional,

        /// <summary>
        /// Emits record properties as init-only members inside the record body.
        /// </summary>
        Init
    }
}
