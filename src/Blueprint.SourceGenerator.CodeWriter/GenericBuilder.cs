using System.Collections.Generic;

namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Represents generic builder.
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

        public string Name { get; }

        /// <summary>
        /// Performs the new operation.
        /// </summary>
        /// <returns>The generated result.</returns>
        public GenericBuilder New()
        {
            _newDefault = true;
            return this;
        }

        /// <summary>
        /// Performs the class operation.
        /// </summary>
        /// <returns>The generated result.</returns>
        public GenericBuilder Class()
        {
            _isClass = true;
            return this;
        }

        /// <summary>
        /// Adds constraint.
        /// </summary>
        /// <param name="constraint">The constraint value.</param>
        /// <returns>The generated result.</returns>
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
        /// Converts string.
        /// </summary>
        /// <returns>The generated result.</returns>
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
