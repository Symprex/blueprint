using System.Net;
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
/// Issue #200, against a real PostgreSQL through <see cref="MartenUnitOfWork"/>: a find-or-create that loses the race —
/// another request inserts the same document between its lookup and its save — used to be answered with a 409, as its
/// save is refused only after the handler has returned. With <c>[RetryOnConflict]</c> it is rerun in a fresh session,
/// whose lookup finds the winner, and answers with it.
/// </summary>
/// <remarks>
/// <see cref="RequiresPostgresFactAttribute"/>s: skipped, with the reason, where <c>BLUEPRINT_TEST_POSTGRES</c> is not
/// set. <see cref="RetryOnConflictIntegrationTests"/> proves the rerun with no database.
/// </remarks>
public class RetryOnConflictPostgresIntegrationTests
{
    [RequiresPostgresFact]
    public async Task A_find_or_create_that_loses_the_race_is_rerun_and_answers_with_the_winner()
    {
        await using var database = await RaceDatabase.CreateAsync();

        var result = await database.ExecuteAsync<RetryingEnsureClaimCommand>(static command => command.Key = "ACME", "loser");

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("winner");
        (await database.LoadAsync("ACME"))!.Owner.Should().Be("winner");
        database.Race.Lookups.Should().Be(2, "the rerun looked again and found the winner");
    }

    [RequiresPostgresFact]
    public async Task A_find_or_create_that_loses_the_race_without_the_attribute_is_answered_with_its_409()
    {
        await using var database = await RaceDatabase.CreateAsync();

        var result = await database.ExecuteAsync<NotRetryingEnsureClaimCommand>(static command => command.Key = "ACME", "loser");

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>()
            .Which.HttpStatus.Should().Be(409);
        (await database.LoadAsync("ACME"))!.Owner.Should().Be("winner");
        database.Race.Lookups.Should().Be(1);
    }

    /// <summary>A Marten store on a schema of its own, dropped when disposed, and the pipeline services around it.</summary>
    private sealed class RaceDatabase : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _schema;
        private readonly DocumentStore _store;
        private readonly ServiceProvider _provider;

        private RaceDatabase(string connectionString, string schema, DocumentStore store, ServiceProvider provider)
        {
            this._connectionString = connectionString;
            this._schema = schema;
            this._store = store;
            this._provider = provider;
        }

        public ClaimRace Race => this._provider.GetRequiredService<ClaimRace>();

        public static Task<RaceDatabase> CreateAsync()
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
            services.AddSingleton<IDocumentStore>(store);
            services.AddSingleton<ClaimRace>();
            services.AddScoped<IDocumentSession>(_ => store.LightweightSession());
            services.AddScoped<IUnitOfWork>(static sp => new MartenUnitOfWork(sp.GetRequiredService<IDocumentSession>(), sp));

            return Task.FromResult(new RaceDatabase(connectionString, schema, store, services.BuildServiceProvider()));
        }

        /// <summary>
        /// Runs <typeparamref name="TOperation"/>'s generated pipeline in a scope of its own, as <paramref name="owner"/>.
        /// </summary>
        public async Task<OperationResult> ExecuteAsync<TOperation>(Action<TOperation> configure, string owner)
            where TOperation : IEnsureClaim
        {
            var app = this._provider.GetRequiredService<IntegrationTestBlueprintApp>();
            var executor = this._provider.GetRequiredService<BlueprintAppExecutor>();
            await using var scope = this._provider.CreateAsyncScope();
            var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(TOperation)), CancellationToken.None)
            {
                SkipAuthorisation = true,
            };
            var operation = (TOperation)context.Operation;
            configure(operation);
            operation.Owner = owner;

            return await executor.GetPipeline(typeof(TOperation)).ExecuteAsync(context);
        }

        /// <summary>Loads the claim <paramref name="key"/> in a session of its own, so only what was committed.</summary>
        public async Task<RaceClaim?> LoadAsync(string key)
        {
            await using var session = this._store.QuerySession();

            return await session.LoadAsync<RaceClaim>(key);
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
/// Stands in for the other request of the race: the first time a lookup finds nothing, it inserts the winner's claim in
/// a session of its own, after the lookup and before the loser's save.
/// </summary>
public sealed class ClaimRace(IDocumentStore store)
{
    private int _lookups;

    /// <summary>Gets how many lookups the handlers made.</summary>
    public int Lookups => this._lookups;

    /// <summary>Looks the claim up, letting the winner in after the first lookup that finds nothing.</summary>
    /// <param name="session">The handler's session.</param>
    /// <param name="key">The claim's key.</param>
    /// <returns>The claim, if it was there when looked up.</returns>
    public async Task<RaceClaim?> LookUpAsync(IDocumentSession session, string key)
    {
        var found = await session.LoadAsync<RaceClaim>(key);

        if (Interlocked.Increment(ref this._lookups) == 1 && found is null)
        {
            await using var winner = store.LightweightSession();
            winner.Insert(new RaceClaim { Id = key, Owner = "winner" });
            await winner.SaveChangesAsync();
        }

        return found;
    }
}

/// <summary>A find-or-create of a claim.</summary>
public interface IEnsureClaim
{
    /// <summary>Gets or sets the claim's key.</summary>
    string Key { get; set; }

    /// <summary>Gets or sets who claims it.</summary>
    string Owner { get; set; }
}

/// <summary>A claim on a key, inserted once: a second insert is refused.</summary>
public sealed class RaceClaim
{
    /// <summary>Gets or sets the key.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets who holds it.</summary>
    public string Owner { get; set; } = string.Empty;
}

/// <summary>Ensures a claim, rerun once if its save is refused.</summary>
[RetryOnConflict]
public partial class RetryingEnsureClaimCommand : ICommand<string>, IEnsureClaim
{
    /// <inheritdoc />
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Owner { get; set; } = string.Empty;

    /// <summary>Finds the claim, or creates it for <see cref="Owner"/>.</summary>
    /// <param name="operation">The command instance.</param>
    /// <param name="session">The scope's session.</param>
    /// <param name="race">The race.</param>
    /// <returns>Who holds the claim.</returns>
    public static async Task<string> HandleAsync(RetryingEnsureClaimCommand operation, IDocumentSession session, ClaimRace race)
    {
        return await EnsureClaim.HandleAsync(operation, session, race);
    }
}

/// <summary>The same find-or-create, not rerun.</summary>
[ConcurrencyConflictError(nameof(Claimed))]
public partial class NotRetryingEnsureClaimCommand : ICommand<string>, IEnsureClaim
{
    /// <summary>Gets the error for a claim another request made first.</summary>
    public static ApiExceptionFactory Claimed => new("Claimed", "claimed", HttpStatusCode.Conflict);

    /// <inheritdoc />
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Owner { get; set; } = string.Empty;

    /// <summary>Finds the claim, or creates it for <see cref="Owner"/>.</summary>
    /// <param name="operation">The command instance.</param>
    /// <param name="session">The scope's session.</param>
    /// <param name="race">The race.</param>
    /// <returns>Who holds the claim.</returns>
    public static async Task<string> HandleAsync(NotRetryingEnsureClaimCommand operation, IDocumentSession session, ClaimRace race)
    {
        return await EnsureClaim.HandleAsync(operation, session, race);
    }
}

/// <summary>The find-or-create both commands run.</summary>
internal static class EnsureClaim
{
    /// <summary>Finds the claim, or inserts it for the operation's owner.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="session">The scope's session.</param>
    /// <param name="race">The race.</param>
    /// <returns>Who holds the claim.</returns>
    public static async Task<string> HandleAsync(IEnsureClaim operation, IDocumentSession session, ClaimRace race)
    {
        if (await race.LookUpAsync(session, operation.Key) is { } existing)
        {
            return existing.Owner;
        }

        session.Insert(new RaceClaim { Id = operation.Key, Owner = operation.Owner });

        return operation.Owner;
    }
}
