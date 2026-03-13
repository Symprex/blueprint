namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Controls how a generated member should represent its value state when emitted.
    /// </summary>
    public enum ValueType
    {
        /// <summary>
        /// Uses the value exactly as configured by the caller.
        /// </summary>
        UserSpecified,

        /// <summary>
        /// Emits the C# default value expression for the member type.
        /// </summary>
        Default,

        /// <summary>
        /// Emits a null value for the member.
        /// </summary>
        Null
    }
}
