using Blueprint.Marten;
using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves, against a real PostgreSQL through <see cref="MartenUnitOfWork"/>, that a document a query's handler stores in
/// the scope's Marten session is persisted by the query's generated pipeline, as legacy's
/// <c>SaveMartenChangesMiddleware</c> persisted it after any operation without <c>[DoNotSaveChanges]</c> (Signature365
/// <c>8b40ecc961</c>, <c>Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29</c>; Portal's
/// <c>Microsoft365AddOnConnectQuery.cs:64</c> stores one) — and that one the query marked with it stores is not.
/// </summary>
/// <remarks>
/// These need a database, so they are <see cref="RequiresPostgresFactAttribute"/>s: skipped, with the reason, where
/// <c>BLUEPRINT_TEST_POSTGRES</c> is not set, as in CI. <see cref="QueryUnitOfWorkIntegrationTests"/> proves the same
/// decision with no database.
/// </remarks>
public class QueryUnitOfWorkPostgresIntegrationTests
{
    [RequiresPostgresFact]
    public async Task When_a_query_stores_a_document_then_its_pipeline_saves_it()
    {
        await using var database = await QueryDocumentDatabase.CreateAsync();

        var result = await database.ExecuteAsync<StoreQueryDocumentQuery>(static query => query.Id = "stored-by-a-query");

        result.WasSuccessful.Should().BeTrue();
        (await database.LoadAsync("stored-by-a-query")).Should().NotBeNull("the query's pipeline saved the session");
    }

    [RequiresPostgresFact]
    public async Task When_a_query_marked_DoNotSaveChanges_stores_a_document_then_its_pipeline_does_not_save_it()
    {
        await using var database = await QueryDocumentDatabase.CreateAsync();

        var result = await database.ExecuteAsync<StoreQueryDocumentWithoutSavingQuery>(static query => query.Id = "stored-but-not-saved");

        result.WasSuccessful.Should().BeTrue();
        (await database.LoadAsync("stored-but-not-saved")).Should().BeNull("[DoNotSaveChanges] leaves the session unsaved");
    }

    /// <summary>A Marten store on a schema of its own, dropped when disposed, and the pipeline services around it.</summary>
    private sealed class QueryDocumentDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _schema;
        private readonly DocumentStore _store;
        private readonly ServiceProvider _provider;

        private QueryDocumentDatabase(string connectionString, string schema, DocumentStore store, ServiceProvider provider)
        {
            this._connectionString = connectionString;
            this._schema = schema;
            this._store = store;
            this._provider = provider;
        }

        public static Task<QueryDocumentDatabase> CreateAsync()
        {
            var connectionString = RequiresPostgresFactAttribute.ConnectionString
                                   ?? throw new InvalidOperationException($"{RequiresPostgresFactAttribute.ConnectionStringVariable} is not set.");
            var schema = "bp_test_" + Guid.NewGuid().ToString("N")[..12];

            var options = new StoreOptions();
            options.Connection(connectionString);
            options.DatabaseSchemaName = schema;
            options.AutoCreateSchemaObjects = AutoCreate.All;
            var store = new DocumentStore(options);

            var services = new ServiceCollection();
            services.AddSingleton<IntegrationTestBlueprintApp>();
            services.AddSingleton<BlueprintAppExecutor>(static sp =>
                new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, sp.GetRequiredService<IntegrationTestBlueprintApp>(), sp));
            services.AddScoped<IDocumentSession>(_ => store.LightweightSession());
            services.AddScoped<IUnitOfWork>(static sp => new MartenUnitOfWork(sp.GetRequiredService<IDocumentSession>(), sp));

            return Task.FromResult(new QueryDocumentDatabase(connectionString, schema, store, services.BuildServiceProvider()));
        }

        /// <summary>Runs <typeparamref name="TOperation"/>'s generated pipeline in a scope of its own.</summary>
        public async Task<OperationResult> ExecuteAsync<TOperation>(Action<TOperation> configure)
        {
            var app = this._provider.GetRequiredService<IntegrationTestBlueprintApp>();
            var executor = this._provider.GetRequiredService<BlueprintAppExecutor>();
            await using var scope = this._provider.CreateAsyncScope();
            var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(TOperation)), CancellationToken.None);
            configure((TOperation)context.Operation);

            return await executor.GetPipeline(typeof(TOperation)).ExecuteAsync(context);
        }

        /// <summary>Loads the document <paramref name="id"/> in a session of its own, so only what was committed.</summary>
        public async Task<QueryStoredDocument?> LoadAsync(string id)
        {
            await using var session = this._store.QuerySession();

            return await session.LoadAsync<QueryStoredDocument>(id);
        }

        public async ValueTask DisposeAsync()
        {
            await this._provider.DisposeAsync();
            this._store.Dispose();

            await using var connection = new NpgsqlConnection(this._connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"drop schema if exists {this._schema} cascade", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>
/// A fact that needs a real PostgreSQL database: it runs only when <see cref="ConnectionStringVariable"/> holds a
/// connection string, and is reported as skipped, with the reason, when it does not — as <c>Blueprint.Marten.Tests</c>'
/// <c>PostgresFactAttribute</c> does.
/// </summary>
public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    /// <summary>The environment variable that holds the connection string of a disposable database.</summary>
    public const string ConnectionStringVariable = "BLUEPRINT_TEST_POSTGRES";

    /// <summary>Initialises the attribute, skipping the test when no connection string is set.</summary>
    public RequiresPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            this.Skip = $"Needs PostgreSQL: set {ConnectionStringVariable} to the connection string of a disposable database.";
        }
    }

    /// <summary>Gets the connection string <see cref="ConnectionStringVariable"/> holds, or <see langword="null"/>.</summary>
    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);
}

/// <summary>A document a query's handler stores (R138).</summary>
public sealed class QueryStoredDocument
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>A query whose handler stores a document in the scope's Marten session.</summary>
public partial class StoreQueryDocumentQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(StoreQueryDocumentQuery operation, IDocumentSession session)
    {
        session.Store(new QueryStoredDocument { Id = operation.Id });

        return operation.Id;
    }
}

/// <summary>A query whose handler stores a document in the scope's Marten session, marked <c>[DoNotSaveChanges]</c>.</summary>
[DoNotSaveChanges]
public partial class StoreQueryDocumentWithoutSavingQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(StoreQueryDocumentWithoutSavingQuery operation, IDocumentSession session)
    {
        session.Store(new QueryStoredDocument { Id = operation.Id });

        return operation.Id;
    }
}
