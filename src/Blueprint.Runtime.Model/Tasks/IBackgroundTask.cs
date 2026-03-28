using Blueprint.Model;
using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Marks an operation as work that belongs to Blueprint's task-oriented host surface, so compile-time discovery can
/// keep that host-specific intent explicit before the fuller background-task runtime and scheduler model lands.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IBackgroundTask : IOperation;
