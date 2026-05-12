using Blueprint.Model;

namespace Blueprint.Testing;

/// <summary>
/// A reusable factory that creates <see cref="BlueprintTestFixture{TApp,TExec}"/> instances for
/// integration tests. Designed to be used as an xUnit <c>IClassFixture&lt;T&gt;</c> so the
/// host is initialised once per test class and each test gets a fresh fixture via
/// <see cref="Create"/>.
/// <para>
/// This mirrors the Signature 365 <c>WebApiFixtureFactory&lt;TStartup&gt;</c> pattern:
/// the factory is injected by xUnit, each test method calls <see cref="Create"/> to get
/// a fixture with fresh test doubles, and the factory disposes the underlying host when
/// the test class completes.
/// </para>
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass.</typeparam>
/// <typeparam name="TExec">The source-generated executor type.</typeparam>
/// <example>
/// <code>
/// // 1. Create a concrete factory (often in a shared test-helpers project)
/// public class PortalTestFixtureFactory : BlueprintTestFixtureFactory&lt;PortalApp, PortalAppExecutor&gt;
/// {
///     protected override void ConfigureServices(IServiceCollection services)
///     {
///         services.AddSingleton&lt;IMartenSessionFactory, FakeMartenSessionFactory&gt;();
///     }
///
///     protected override Task ConfigureOnceAsync(IServiceProvider services)
///     {
///         // Run database migrations etc.
///         return Task.CompletedTask;
///     }
/// }
///
/// // 2. Use in tests
/// public class GetTemplateQueryTests : IClassFixture&lt;PortalTestFixtureFactory&gt;
/// {
///     private readonly PortalTestFixtureFactory _factory;
///
///     public GetTemplateQueryTests(PortalTestFixtureFactory factory) =&gt; _factory = factory;
///
///     [Fact]
///     public async Task When_valid_then_returns_template()
///     {
///         var api = _factory.Create();
///         api.AuthenticateAsTenantAdministrator(tenantId);
///
///         var result = await api.QueryAsync(new GetTemplateQuery { Id = "t1" });
///         result.Should().NotBeNull();
///     }
/// }
/// </code>
/// </example>
public abstract class BlueprintTestFixtureFactory<TApp, TExec> : IDisposable, IAsyncDisposable
    where TApp : BlueprintApp
    where TExec : BlueprintAppExecutor
{
    private readonly object _initLock = new();
    private readonly List<IDisposable> _createdFixtures = new();
    private BlueprintTestHost<TApp, TExec>? _host;
    private bool _initialised;
    private Exception? _initialisationException;

    /// <summary>
    /// Gets the maximum time allowed for one-time initialisation (via <see cref="ConfigureOnceAsync"/>)
    /// before the factory fails fast with a <see cref="TimeoutException"/>. Default is 30 seconds.
    /// Override for slower setups such as database migration.
    /// </summary>
    protected virtual TimeSpan ConfigureOnceTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a new <see cref="BlueprintTestFixture{TApp,TExec}"/> backed by the shared host.
    /// Each fixture gets its own <see cref="TestAuthenticationManager"/> and fresh test-double
    /// state, so tests are isolated even when sharing the factory.
    /// </summary>
    /// <param name="configure">
    /// Optional per-fixture DI overrides. These are applied AFTER <see cref="ConfigureServices"/>
    /// and <see cref="ConfigureTestDefaults"/> but before the host is built (on first call).
    /// </param>
    /// <returns>A ready-to-use test fixture.</returns>
    public BlueprintTestFixture<TApp, TExec> Create(Action<IServiceCollection>? configure = null)
    {
        this.EnsureInitialised(configure);

        var fixture = new BlueprintTestFixture<TApp, TExec>(this._host!);
        this._createdFixtures.Add(fixture);

        return fixture;
    }

    /// <summary>
    /// Override to register application-specific services, mocks, and test doubles. Called once
    /// during host initialisation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>
    /// Override to register test-specific doubles beyond what <see cref="BlueprintTestHostBuilder{TApp,TExec}.WithTestDefaults"/>
    /// provides. Called once during host initialisation, after <see cref="ConfigureServices"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    protected virtual void ConfigureTestDefaults(IServiceCollection services)
    {
    }

    /// <summary>
    /// Override to run one-time setup actions after the host is built (e.g. database migration,
    /// seed data). Called once, inside the initialisation lock, with a scoped service provider.
    /// </summary>
    /// <param name="services">A scoped service provider for resolving services.</param>
    /// <returns>A task representing the async operation.</returns>
    protected virtual Task ConfigureOnceAsync(IServiceProvider services)
    {
        return Task.CompletedTask;
    }

    private void EnsureInitialised(Action<IServiceCollection>? perFixtureConfigure)
    {
        if (this._initialised)
        {
            return;
        }

        lock (this._initLock)
        {
            if (this._initialised)
            {
                return;
            }

            if (this._initialisationException is not null)
            {
                throw this._initialisationException;
            }

            try
            {
                var builder = BlueprintTestHostBuilder<TApp, TExec>.Create()
                    .WithTestDefaults()
                    .ConfigureServices(this.ConfigureServices)
                    .ConfigureServices(this.ConfigureTestDefaults);

                if (perFixtureConfigure is not null)
                {
                    builder.ConfigureServices(perFixtureConfigure);
                }

                this._host = builder.Build();

                // Run one-time async setup (e.g. DB migration)
                var configureOnceTask = Task.Run(async () =>
                {
                    using var scope = this._host.Services.CreateScope();
                    await this.ConfigureOnceAsync(scope.ServiceProvider);
                });

                var completedTask = Task.WhenAny(configureOnceTask, Task.Delay(this.ConfigureOnceTimeout))
                    .GetAwaiter()
                    .GetResult();

                if (completedTask != configureOnceTask)
                {
                    throw new TimeoutException(
                        $"Timed out after {this.ConfigureOnceTimeout} while initialising the " +
                        $"{typeof(TApp).Name} test fixture factory. This usually means application " +
                        "startup or a local integration dependency is hung.");
                }

                configureOnceTask.GetAwaiter().GetResult();

                this._initialised = true;
            }
            catch (Exception e)
            {
                this._initialisationException = e;
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var d in this._createdFixtures)
        {
            d.Dispose();
        }

        this._host?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var d in this._createdFixtures)
        {
            d.Dispose();
        }

        if (this._host is not null)
        {
            await this._host.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
