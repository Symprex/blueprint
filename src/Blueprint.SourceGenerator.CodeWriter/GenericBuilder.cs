using System.Collections.Generic;

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

        internal GenericBuilder(string name)
        {
            Name = name;
            _constraints = new List<string>();
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
            _newDefault = true;
            return this;
        }

        /// <summary>
        /// Adds the class generic constraint.
        /// </summary>
        /// <returns>The same builder so fluent configuration can continue.</returns>
        public GenericBuilder Class()
        {
            _isClass = true;
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
                case "new()": return New();
                case "class": return Class();
                default:
                    if (!(string.IsNullOrWhiteSpace(constraint) || _constraints.Contains(constraint)))
                        _constraints.Add(constraint);
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
            if (_isClass)
                _constraints.Insert(0, "class");
            if (_newDefault)
                _constraints.Insert(0, "new()");

            var output = string.Join(", ", _constraints).Trim();
            if(string.IsNullOrEmpty(output))
                return string.Empty;

            return $"where {Name} : {output}";
        }
    }
}
