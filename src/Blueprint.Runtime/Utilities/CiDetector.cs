namespace Blueprint.Utilities;

/// <summary>
/// Detects whether the current process is running inside a continuous
/// integration environment by probing well-known environment variables
/// from common CI platforms (GitHub Actions, GitLab CI, Azure DevOps,
/// TeamCity, AppVeyor, CircleCI, Travis, Buildkite, etc.).
/// </summary>
public static class CiDetector
{
    /// <summary>
    /// Gets a value indicating whether the current process appears to be running
    /// on a CI server.
    /// </summary>
    public static bool IsRunningOnCiServer =>
        IsEnvironmentVariableDefined("CI") ||
        IsEnvironmentVariableDefined("GITHUB_ACTIONS") ||
        IsEnvironmentVariableDefined("GITLAB_CI") ||
        IsEnvironmentVariableDefined("TF_BUILD") ||
        IsEnvironmentVariableDefined("TEAMCITY_VERSION") ||
        IsEnvironmentVariableDefined("APPVEYOR") ||
        IsEnvironmentVariableDefined("CIRCLECI") ||
        IsEnvironmentVariableDefined("TRAVIS") ||
        IsEnvironmentVariableDefined("BUILDKITE") ||
        IsEnvironmentVariableDefined("BUILD_ID") ||
        IsEnvironmentVariableDefined("BUILD_CI") ||
        IsEnvironmentVariableDefined("BITRISE_IO") ||
        IsEnvironmentVariableDefined("HEROKU_TEST_RUN_ID");

    private static bool IsEnvironmentVariableDefined(string name)
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
    }
}
