using JetBrains.Annotations;

namespace Blueprint.Model;

/// <summary>
/// A marker interface that can be placed on an operation class to indicate what type of response
/// it will generate.
/// </summary>
/// <typeparam name="TResponse">The type of response.</typeparam>
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IReturn<TResponse>;
