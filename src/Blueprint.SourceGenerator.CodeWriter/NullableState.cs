using System;
using System.Collections.Generic;
using System.Text;

namespace Blueprint.SourceGenerator.CodeWriter
{
    /// <summary>
    /// Specifies available values for nullable state.
    /// </summary>
    public enum NullableState
    {
        Default,
        Enable,
        Disable,
        Restore
    }
}
