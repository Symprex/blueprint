namespace Blueprint.Model;

/// <summary>
/// Marker interface used to identify operations. Typically an operation will implement one of the
/// <see cref="ICommand"/> or <see cref="IQuery"/> interfaces that both extend this interface.
/// The source generator emits a <c>static ApiOperationDescriptor Configure()</c> method on each
/// concrete <c>partial</c> class — there is no interface-level <c>static abstract</c> member so
/// that abstract generic base classes (e.g. <c>OrderedPagedQuery&lt;T&gt;</c>) can declare
/// <c>IQuery&lt;T&gt;</c> without the C# compiler requiring a static implementation on the
/// non-concrete type.
/// </summary>
public interface IOperation;
