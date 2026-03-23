namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Builds generic parameter constraints so declarations can emit valid where clauses.
    /// </summary>
    public sealed class GenericBuilder
    {
        private List<string> _constraints { get; }
        private bool _isClass;
        private bool _newDefault;

        /// <summary>
        /// Initializes a new instance with the supplied type parameter name.
        /// </summary>
        /// <param name="name">The type parameter name this builder configures.</param>
        internal GenericBuilder(string name)
        {
            this.Name = name;
            this._constraints = new List<string>();
        }

        /// <summary>
        /// Gets the generic type parameter name that this builder configures.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Adds the new() generic constraint.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public GenericBuilder New()
        {
            this._newDefault = true;
            return this;
        }

        /// <summary>
        /// Adds the class generic constraint.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public GenericBuilder Class()
        {
            this._isClass = true;
            return this;
        }

        /// <summary>
        /// Adds an additional generic constraint if it has not already been recorded.
        /// </summary>
        /// <param name="constraint">The constraint used by this operation.</param>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public GenericBuilder AddConstraint(string constraint)
        {
            switch (constraint)
            {
                case "new()": return this.New();
                case "class": return this.Class();
                default:
                    if (!(string.IsNullOrWhiteSpace(constraint) || this._constraints.Contains(constraint)))
                    {
                        this._constraints.Add(constraint);
                    }

                    break;
            }

            return this;
        }

        /// <summary>
        /// Formats the configured generic constraints as a C# where clause.
        /// </summary>
        /// <returns>The generated source fragment represented by the current builder.</returns>
        public override string ToString()
        {
            if (this._isClass)
            {
                this._constraints.Insert(0, "class");
            }

            if (this._newDefault)
            {
                this._constraints.Insert(0, "new()");
            }

            var output = string.Join(", ", this._constraints).Trim();
            return string.IsNullOrEmpty(output) ? string.Empty : $"where {this.Name} : {output}";
        }
    }
}
