using Marten;
using Npgsql;
using Weasel.Core;

namespace Blueprint.Marten.Tests.Postgres;

/// <summary>
/// A real Marten <see cref="DocumentStore"/> on a schema of its own in the database <see cref="PostgresFactAttribute"/>
/// names, with a plain <c>test_outbox</c> table in the same schema, all dropped when the store is disposed.
/// </summary>
/// <remarks>
/// The <c>test_outbox</c> table stands in for a consumer's outbox table: a listener writes a row to it with
/// <see cref="IDocumentOperations.QueueSqlCommand"/>, as Signature365's outbox storage does. A schema per store keeps
/// tests independent of each other and of whatever else the database holds.
/// </remarks>
public sealed class PostgresTestStore : IAsyncDisposable
{
    private readonly string _connectionString;

    private PostgresTestStore(DocumentStore store, string connectionString, string schema)
    {
        this.Store = store;
        this._connectionString = connectionString;
        this.Schema = schema;
    }

    /// <summary>
    /// Gets the document store.
    /// </summary>
    public DocumentStore Store { get; }

    /// <summary>
    /// Gets the schema the store and the <c>test_outbox</c> table live in.
    /// </summary>
    public string Schema { get; }

    /// <summary>
    /// Gets the qualified name of the outbox table, for SQL a listener queues.
    /// </summary>
    public string OutboxTable => this.Schema + ".test_outbox";

    /// <summary>
    /// Creates a store on a new schema, with the <c>test_outbox</c> table created in it.
    /// </summary>
    /// <param name="configure">Configures the store's options further, after the connection and schema are set.</param>
    /// <returns>The store, which the caller must dispose.</returns>
    public static async Task<PostgresTestStore> CreateAsync(Action<StoreOptions>? configure = null)
    {
        var connectionString = PostgresFactAttribute.ConnectionString
                               ?? throw new InvalidOperationException(
                                   $"{PostgresFactAttribute.ConnectionStringVariable} is not set; mark the test [PostgresFact].");

        var schema = "bp_test_" + Guid.NewGuid().ToString("N")[..12];

        await ExecuteAsync(
            connectionString,
            $"create schema {schema}; create table {schema}.test_outbox (id text primary key, body text not null);");

        var options = new StoreOptions();
        options.Connection(connectionString);
        options.DatabaseSchemaName = schema;
        options.AutoCreateSchemaObjects = AutoCreate.All;
        configure?.Invoke(options);

        return new PostgresTestStore(new DocumentStore(options), connectionString, schema);
    }

    /// <summary>
    /// Gets the ids in the <c>test_outbox</c> table, read on a connection of its own, so only what was committed.
    /// </summary>
    /// <returns>The ids, in order.</returns>
    public async Task<IReadOnlyList<string>> ReadOutboxIdsAsync()
    {
        await using var connection = new NpgsqlConnection(this._connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"select id from {this.OutboxTable} order by id", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var ids = new List<string>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    /// <summary>
    /// Inserts a row into the <c>test_outbox</c> table directly, committed at once.
    /// </summary>
    /// <param name="id">The row's id.</param>
    /// <returns>A task that completes when the row is committed.</returns>
    public Task InsertOutboxRowAsync(string id)
    {
        return ExecuteAsync(this._connectionString, $"insert into {this.OutboxTable} (id, body) values ('{id}', 'existing')");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.Store.Dispose();

        await ExecuteAsync(this._connectionString, $"drop schema if exists {this.Schema} cascade");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
