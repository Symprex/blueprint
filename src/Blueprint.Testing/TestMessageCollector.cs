namespace Blueprint.Testing;

/// <summary>
/// A test-only message sink that captures all messages "published" or "sent" during
/// operation execution, without requiring a real message bus (Rebus, MassTransit, etc.).
/// <para>
/// Register this as a singleton in test DI and have handlers or infrastructure call
/// <see cref="Publish{T}"/> or <see cref="Send{T}"/> instead of the real bus. This mirrors
/// the S365 <c>FakeBus</c> pattern but is bus-agnostic.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Typical usage: replace the real <c>IBus</c> with a thin wrapper that delegates to this
/// collector, or use <see cref="BlueprintTestHostBuilder{TApp,TExec}.ConfigureServices"/> to
/// register a mock bus that captures messages here.
/// </para>
/// </remarks>
public sealed class TestMessageCollector
{
    private readonly object _lock = new();

    private readonly List<MessageEnvelope> _published = new();
    private readonly List<MessageEnvelope> _sent = new();

    /// <summary>
    /// Gets all messages that were published (broadcast to subscribers).
    /// </summary>
    public IReadOnlyList<MessageEnvelope> Published => this._published;

    /// <summary>
    /// Gets all messages that were sent (point-to-point delivery).
    /// </summary>
    public IReadOnlyList<MessageEnvelope> Sent => this._sent;

    /// <summary>
    /// Records a published message (broadcast).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message instance.</param>
    public void Publish<T>(T message) where T : notnull
    {
        lock (this._lock)
        {
            this._published.Add(new MessageEnvelope(typeof(T), message));
        }
    }

    /// <summary>
    /// Records a sent message (point-to-point).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message instance.</param>
    public void Send<T>(T message) where T : notnull
    {
        lock (this._lock)
        {
            this._sent.Add(new MessageEnvelope(typeof(T), message));
        }
    }

    /// <summary>
    /// Gets a single published message of the specified type, asserting exactly one exists.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The message instance.</returns>
    public T GetSinglePublished<T>() where T : notnull
    {
        var matches = this._published
            .Where(static e => e.Message is T)
            .Select(static e => (T)e.Message)
            .ToList();

        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                $"Expected a single published message of type {typeof(T).Name} but none were published. " +
                $"Published types: {FormatMessageTypes(this._published)}"),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Expected a single published message of type {typeof(T).Name} but found {matches.Count}."),
        };
    }

    /// <summary>
    /// Gets all published messages of the specified type.
    /// </summary>
    /// <typeparam name="T">The message type to filter by.</typeparam>
    /// <returns>All matching message instances.</returns>
    public IReadOnlyList<T> GetAllPublished<T>() where T : notnull
    {
        return this._published
            .Where(static e => e.Message is T)
            .Select(static e => (T)e.Message)
            .ToList();
    }

    /// <summary>
    /// Gets a single sent message of the specified type, asserting exactly one exists.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The message instance.</returns>
    public T GetSingleSent<T>() where T : notnull
    {
        var matches = this._sent
            .Where(static e => e.Message is T)
            .Select(static e => (T)e.Message)
            .ToList();

        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                $"Expected a single sent message of type {typeof(T).Name} but none were sent. " +
                $"Sent types: {FormatMessageTypes(this._sent)}"),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Expected a single sent message of type {typeof(T).Name} but found {matches.Count}."),
        };
    }

    /// <summary>
    /// Gets all sent messages of the specified type.
    /// </summary>
    /// <typeparam name="T">The message type to filter by.</typeparam>
    /// <returns>All matching message instances.</returns>
    public IReadOnlyList<T> GetAllSent<T>() where T : notnull
    {
        return this._sent
            .Where(static e => e.Message is T)
            .Select(static e => (T)e.Message)
            .ToList();
    }

    /// <summary>
    /// Asserts that no messages of the specified type have been published.
    /// </summary>
    /// <typeparam name="T">The message type that should not have been published.</typeparam>
    public void AssertNonePublished<T>() where T : notnull
    {
        var count = this._published.Count(static e => e.Message is T);
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no published messages of type {typeof(T).Name} but found {count}.");
        }
    }

    /// <summary>
    /// Asserts that no messages of the specified type have been sent.
    /// </summary>
    /// <typeparam name="T">The message type that should not have been sent.</typeparam>
    public void AssertNoneSent<T>() where T : notnull
    {
        var count = this._sent.Count(static e => e.Message is T);
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"Expected no sent messages of type {typeof(T).Name} but found {count}.");
        }
    }

    /// <summary>
    /// Clears all collected messages. Useful between test executions in shared fixture scenarios.
    /// </summary>
    public void ClearAll()
    {
        lock (this._lock)
        {
            this._published.Clear();
            this._sent.Clear();
        }
    }

    /// <summary>
    /// S365-compatible alias for <see cref="ClearAll"/>. S365 tests use <c>api.Bus.Clear()</c>
    /// (56+ call sites) to reset collected events between operations within a single test.
    /// </summary>
    public void Clear()
    {
        this.ClearAll();
    }

    /// <summary>
    /// S365-compatible: returns the single published message of the specified type,
    /// throwing if zero or more than one exist. Matches S365's
    /// <c>bus.Should().HavePublishedSingle&lt;T&gt;()</c>.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The message instance.</returns>
    public T HavePublishedSingle<T>() where T : notnull
    {
        return this.GetSinglePublished<T>();
    }

    /// <summary>
    /// S365-compatible: returns all published messages of the specified type, asserting
    /// at least one exists. Matches S365's <c>bus.Should().HavePublished&lt;T&gt;()</c>
    /// (the overload that returns <c>List&lt;T&gt;</c>).
    /// </summary>
    /// <typeparam name="T">The message type to filter by.</typeparam>
    /// <returns>All matching message instances (guaranteed non-empty).</returns>
    public IReadOnlyList<T> HavePublished<T>() where T : notnull
    {
        var matches = this.GetAllPublished<T>();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expected at least one published message of type {typeof(T).Name} but none were published. " +
                $"Published types: {FormatMessageTypes(this._published)}");
        }

        return matches;
    }

    /// <summary>
    /// S365-compatible: returns all published messages of the specified type (zero or more).
    /// Unlike <see cref="HavePublished{T}"/> this does not assert a minimum count.
    /// Matches S365's <c>bus.Should().HavePublishedAny&lt;T&gt;()</c> but also allows empty.
    /// </summary>
    /// <typeparam name="T">The message type to filter by.</typeparam>
    /// <returns>All matching message instances.</returns>
    public IEnumerable<T> HavePublishedAny<T>() where T : notnull
    {
        var matches = this.GetAllPublished<T>();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expected at least one published message of type {typeof(T).Name} but none were published. " +
                $"Published types: {FormatMessageTypes(this._published)}");
        }

        return matches;
    }

    /// <summary>
    /// S365-compatible: throws if any messages of the specified type were published.
    /// Matches S365's <c>bus.Should().NotHavePublished&lt;T&gt;()</c>.
    /// </summary>
    /// <typeparam name="T">The message type that should not have been published.</typeparam>
    public void NotHavePublished<T>() where T : notnull
    {
        this.AssertNonePublished<T>();
    }

    /// <summary>
    /// S365-compatible: returns the single sent message of the specified type,
    /// throwing if zero or more than one exist. Matches S365's
    /// <c>bus.Should().HaveSent&lt;T&gt;()</c> which returns a single T.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The message instance.</returns>
    public T HaveSent<T>() where T : notnull
    {
        return this.GetSingleSent<T>();
    }

    /// <summary>
    /// Returns the single sent message of the specified type. Identical to
    /// <see cref="HaveSent{T}"/> but with an explicit "Single" name for clarity.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The message instance.</returns>
    public T HaveSentSingle<T>() where T : notnull
    {
        return this.GetSingleSent<T>();
    }

    /// <summary>
    /// S365-compatible: throws if any messages of the specified type were sent.
    /// Matches S365's <c>bus.Should().NotHaveSent&lt;T&gt;()</c>.
    /// </summary>
    /// <typeparam name="T">The message type that should not have been sent.</typeparam>
    public void NotHaveSent<T>() where T : notnull
    {
        this.AssertNoneSent<T>();
    }

    private static string FormatMessageTypes(List<MessageEnvelope> envelopes)
    {
        if (envelopes.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", envelopes.Select(static e => e.MessageType.Name).Distinct());
    }

    /// <summary>
    /// Returns <c>this</c> collector for FluentAssertions-style chaining, so S365 test code
    /// like <c>api.Bus.Should().HavePublishedSingle&lt;T&gt;()</c> works without modification.
    /// The assertion methods (<see cref="HavePublishedSingle{T}"/>, <see cref="HavePublished{T}"/>,
    /// <see cref="NotHavePublished{T}"/>, etc.) are already defined on this class.
    /// </summary>
    /// <returns>This collector, for method chaining.</returns>
    public TestMessageCollector Should()
    {
        return this;
    }

    /// <summary>
    /// Wraps a captured message with its declared type for accurate type filtering.
    /// </summary>
    /// <param name="MessageType">The declared message type (the <c>T</c> in <c>Publish&lt;T&gt;</c>).</param>
    /// <param name="Message">The message instance.</param>
    public sealed record MessageEnvelope(Type MessageType, object Message);
}
