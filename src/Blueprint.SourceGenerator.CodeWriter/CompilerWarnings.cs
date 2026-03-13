namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Provides well-known compiler warning identifiers that generated code may intentionally suppress.
    /// </summary>
    public class CompilerWarnings
    {
        /// <summary>
        /// Warning ID for fields that are declared but never used. Generated backing fields sometimes intentionally trigger this warning.
        /// </summary>
        public const string CS0169 = nameof(CS0169);

        /// <summary>
        /// Warning ID for obsolete members that do not include a custom message.
        /// </summary>
        public const string CS0612 = nameof(CS0612);

        /// <summary>
        /// Warning ID for obsolete members that include a custom message.
        /// </summary>
        public const string CS0618 = nameof(CS0618);
    }
}
