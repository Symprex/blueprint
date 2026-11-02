namespace Blueprint.Model;

/// <summary>
/// Spike stand-in for the minimal-mode attribute #65 adds to <c>Blueprint.Model</c> (Symprex/blueprint#75, D9 and D25):
/// the generator recognises it by its metadata name, so declaring it here adds no public API.
/// </summary>
/// <typeparam name="TInner">The inner operation whose properties the decorated operation exposes.</typeparam>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExposeAttribute<TInner> : Attribute
    where TInner : IOperation;
