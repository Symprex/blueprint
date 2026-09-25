namespace Blueprint.Marten.Tests.Postgres;

/// <summary>
/// A fact that needs a real PostgreSQL database. It runs only when the <see cref="ConnectionStringVariable"/> environment
/// variable holds a connection string, and is reported as skipped, with the reason, when it does not.
/// </summary>
/// <remarks>
/// <para>
/// This is how this repository marks a test that needs a database. It is opt-in and has no default connection string,
/// so a run without a database reports these tests as skipped rather than failing them, and never connects to a
/// database nobody named. CI provides no PostgreSQL and sets no such variable, so it reports them as skipped.
/// </para>
/// <para>
/// To run them, point the variable at a disposable database the user may create schemas in, for example the local
/// Docker PostgreSQL: <c>Host=localhost;Port=5432;Database=blueprint_source_gen_tests;Username=...;Password=...</c>. Each
/// test works in a schema of its own, which <see cref="PostgresTestStore"/> drops when it is disposed.
/// </para>
/// </remarks>
public sealed class PostgresFactAttribute : Xunit.FactAttribute
{
    /// <summary>
    /// The environment variable that holds the connection string of the database these tests run against.
    /// </summary>
    public const string ConnectionStringVariable = "BLUEPRINT_TEST_POSTGRES";

    /// <summary>
    /// Initialises the attribute, skipping the test when no connection string is set.
    /// </summary>
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            this.Skip = $"Needs PostgreSQL: set {ConnectionStringVariable} to the connection string of a disposable database.";
        }
    }

    /// <summary>
    /// Gets the connection string <see cref="ConnectionStringVariable"/> holds, or <see langword="null"/> when it is not set.
    /// </summary>
    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);
}
