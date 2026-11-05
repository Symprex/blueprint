using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Hangfire.Storage;
using JetBrains.Annotations;

namespace Blueprint.Hangfire;

/// <summary>
/// Describes a Hangfire job this package created by the background task it carries, from the job's stored
/// <see cref="InvocationData"/> alone, so a monitoring app can name and show a job without referencing the task's assembly.
/// </summary>
/// <remarks>
/// <para>
/// Every job <see cref="HangfireBackgroundTaskScheduleProvider"/> and <see cref="HangfireRecurringTaskProvider"/> create
/// invokes <see cref="HangfireBackgroundTaskExecutor.ExecuteAsync"/> with a serialised payload, so without this every one of
/// them would be named after that method. The payload's format belongs to this package, and this is the supported way to
/// read it.
/// </para>
/// <para>
/// The task's type is never loaded: its name is read from the type name the payload records.
/// </para>
/// </remarks>
[PublicAPI]
public static class HangfireTaskJobDescriber
{
    private static readonly string ExecutorTypeName = typeof(HangfireBackgroundTaskExecutor).FullName!;

    /// <summary>
    /// Describes the job <paramref name="invocationData"/> stores, when it is one this package created.
    /// </summary>
    /// <param name="invocationData">The job's stored invocation data, as Hangfire's monitoring API returns it.</param>
    /// <param name="description">The task the job carries, when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the job invokes <see cref="HangfireBackgroundTaskExecutor.ExecuteAsync"/> with a payload
    /// that can be read; <see langword="false"/> for any other job, such as one of Hangfire's own or one legacy Blueprint wrote.
    /// </returns>
    public static bool TryDescribe(InvocationData invocationData, [NotNullWhen(true)] out HangfireTaskJobDescription? description)
    {
        ArgumentNullException.ThrowIfNull(invocationData);

        description = null;

        if (invocationData.Method != nameof(HangfireBackgroundTaskExecutor.ExecuteAsync) ||
            invocationData.Type is null ||
            !(invocationData.Type == ExecutorTypeName || invocationData.Type.StartsWith(ExecutorTypeName + ",", StringComparison.Ordinal)) ||
            string.IsNullOrEmpty(invocationData.Arguments))
        {
            return false;
        }

        try
        {
            // Hangfire serialises each argument on its own and stores the array of them, so the payload, a string, is
            // held as a JSON string literal.
            var arguments = JsonSerializer.Deserialize<string?[]>(invocationData.Arguments);

            if (arguments is not [{ } serialisedPayload, ..] ||
                JsonSerializer.Deserialize<string>(serialisedPayload) is not { } payloadJson ||
                JsonSerializer.Deserialize<HangfireTaskPayload>(payloadJson) is not { TaskTypeName.Length: > 0 } payload)
            {
                return false;
            }

            description = new HangfireTaskJobDescription(GetShortTypeName(payload.TaskTypeName), payload.TaskTypeName, payload.TaskJson);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes the type's own name from its assembly-qualified name, without its namespace, any declaring type, any
    /// generic arguments or its assembly: <c>Some.Namespace.Outer+MyTask`1[[Some.Model, Models]], Some.Assembly</c> →
    /// <c>MyTask`1</c>.
    /// </summary>
    private static string GetShortTypeName(string assemblyQualifiedName)
    {
        // The type's full name runs up to its generic arguments or, without any, its assembly.
        var end = assemblyQualifiedName.IndexOfAny(['[', ',']);
        var fullName = end < 0 ? assemblyQualifiedName : assemblyQualifiedName[..end];

        return fullName[(fullName.LastIndexOfAny(['.', '+']) + 1)..].Trim();
    }
}
