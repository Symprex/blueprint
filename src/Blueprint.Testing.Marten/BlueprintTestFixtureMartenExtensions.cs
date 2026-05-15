using Marten;
using Marten.Linq.SoftDeletes;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Testing.Marten;

/// <summary>
/// Marten database extension methods for <see cref="BlueprintTestFixture{TApp,TExec}"/> that provide
/// multi-tenant document session access during integration tests. These methods mirror the
/// Signature 365 <c>ApiFixtureDbExtensions</c> patterns so S365-owned test helpers can delegate
/// to these without changing every test file.
/// <para>
/// Tenant-aware overloads resolve <see cref="IDocumentStore"/> from DI and open a lightweight
/// session scoped to the given tenant identifier. Tenant-free overloads open a session using
/// the default tenant.
/// </para>
/// </summary>
public static class BlueprintTestFixtureMartenExtensions
{
    /// <summary>
    /// Opens a lightweight Marten session scoped to <paramref name="tenantId"/> and passes it
    /// to <paramref name="action"/>. The session is disposed after the action completes.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="tenantId">The tenant identifier for the Marten session.</param>
    /// <param name="action">The async action to execute with the document session.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WithDatabaseAsync<TApp, TExec>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        string tenantId,
        Func<IDocumentSession, Task> action)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
    {
        using var scope = fixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession(tenantId);
        await action(db);
    }

    /// <summary>
    /// Opens a lightweight Marten session scoped to <paramref name="tenantId"/>, executes
    /// <paramref name="action"/>, and returns its result.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="tenantId">The tenant identifier for the Marten session.</param>
    /// <param name="action">The async function to execute with the document session.</param>
    /// <returns>The result of <paramref name="action"/>.</returns>
    public static async Task<T> WithDatabaseAsync<TApp, TExec, T>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        string tenantId,
        Func<IDocumentSession, Task<T>> action)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
    {
        await using var db = fixture.Services.GetRequiredService<IDocumentStore>().LightweightSession(tenantId);
        return await action(db);
    }

    /// <summary>
    /// Opens a lightweight Marten session scoped to <paramref name="tenantId"/> along with a
    /// scoped <see cref="IServiceProvider"/>, executes <paramref name="action"/>, and returns
    /// its result. Useful when the action needs to resolve additional services.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="tenantId">The tenant identifier for the Marten session.</param>
    /// <param name="action">The async function to execute with the document session and service provider.</param>
    /// <returns>The result of <paramref name="action"/>.</returns>
    public static async Task<T> WithDatabaseAsync<TApp, TExec, T>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        string tenantId,
        Func<IDocumentSession, IServiceProvider, Task<T>> action)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
    {
        using var scope = fixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession(tenantId);
        return await action(db, scope.ServiceProvider);
    }

    /// <summary>
    /// Opens a lightweight Marten session using the default tenant and passes it to
    /// <paramref name="action"/>.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="action">The async action to execute with the document session.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WithDatabaseAsync<TApp, TExec>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        Func<IDocumentSession, Task> action)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
    {
        await using var db = fixture.Services.GetRequiredService<IDocumentStore>().LightweightSession();
        await action(db);
    }

    /// <summary>
    /// Opens a lightweight Marten session using the default tenant along with a scoped
    /// <see cref="IServiceProvider"/>, executes <paramref name="action"/>, and returns its result.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="action">The async function to execute with the document session and service provider.</param>
    /// <returns>The result of <paramref name="action"/>.</returns>
    public static async Task<T> WithDatabaseAsync<TApp, TExec, T>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        Func<IDocumentSession, IServiceProvider, Task<T>> action)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
    {
        using var scope = fixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        return await action(db, scope.ServiceProvider);
    }

    /// <summary>
    /// Loads a single document by <paramref name="id"/> from a Marten session scoped to
    /// <paramref name="tenantId"/>.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <typeparam name="T">The document type to load.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="tenantId">The tenant identifier for the Marten session.</param>
    /// <param name="id">The string document identifier.</param>
    /// <returns>The loaded document, or <c>null</c> if not found.</returns>
    public static async Task<T?> LoadFromDatabaseAsync<TApp, TExec, T>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        string tenantId,
        string id)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
        where T : notnull
    {
        await using var db = fixture.Services.GetRequiredService<IDocumentStore>().LightweightSession(tenantId);
        return await db.LoadAsync<T>(id);
    }

    /// <summary>
    /// Queries all documents of type <typeparamref name="T"/> from a Marten session scoped to
    /// <paramref name="tenantId"/>.
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <typeparam name="T">The document type to query.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="tenantId">The tenant identifier for the Marten session.</param>
    /// <returns>A read-only list of all documents of the specified type for the given tenant.</returns>
    public static async Task<IReadOnlyList<T>> AllAsync<TApp, TExec, T>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        string tenantId)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
        where T : notnull
    {
        using var scope = fixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession(tenantId);
        return await db.Query<T>().ToListAsync();
    }

    /// <summary>
    /// Queries all documents of type <typeparamref name="T"/> including soft-deleted documents,
    /// from a Marten session scoped to <paramref name="tenantId"/>. When the document type uses
    /// Marten's soft-delete feature, <c>MaybeDeleted()</c> is applied; otherwise a plain query
    /// is used (avoiding SQL errors from missing <c>mt_deleted</c> columns).
    /// </summary>
    /// <typeparam name="TApp">The Blueprint app type.</typeparam>
    /// <typeparam name="TExec">The Blueprint executor type.</typeparam>
    /// <typeparam name="T">The document type to query.</typeparam>
    /// <param name="fixture">The test fixture providing DI services.</param>
    /// <param name="tenantId">The tenant identifier for the Marten session.</param>
    /// <returns>A read-only list of all documents including soft-deleted ones.</returns>
    public static async Task<IReadOnlyList<T>> AllIncludingDeletedAsync<TApp, TExec, T>(
        this BlueprintTestFixture<TApp, TExec> fixture,
        string tenantId)
        where TApp : Model.BlueprintApp
        where TExec : BlueprintAppExecutor
        where T : notnull
    {
        using var scope = fixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDocumentStore>().LightweightSession(tenantId);

        var documentProvider = db.DocumentStore.Storage.Database.Providers.StorageFor<T>();

        // Check whether this is a SoftDeleted document type. If not, MaybeDeleted() would fail
        // because the mt_deleted column does not exist.
        if (documentProvider.IdentityMap.DeleteFragment.GetType().Name != "SoftDelete")
        {
            return await db.Query<T>().ToListAsync();
        }

        return await db.Query<T>().Where(static t => t.MaybeDeleted()).ToListAsync();
    }
}
