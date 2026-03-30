namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// The resolved set of ordered contributors for a generator configuration.
/// </summary>
/// <param name="PostInitializationContributors">The post-initialization contributors.</param>
/// <param name="DescriptorContributors">The descriptor contributors.</param>
/// <param name="ExceptionTranslationContributors">The exception-translation contributors.</param>
/// <param name="PipelineContributors">The pipeline contributors.</param>
/// <param name="HostContributors">The host contributors.</param>
/// <param name="AppRegistrationContributors">The app-registration contributors.</param>
/// <param name="DebugManifestContributors">The debug-manifest contributors.</param>
public sealed record BlueprintModuleExecutionPlan(
    IPostInitializationContributor[] PostInitializationContributors,
    IDescriptorContributor[] DescriptorContributors,
    IExceptionTranslationContributor[] ExceptionTranslationContributors,
    IPipelineContributor[] PipelineContributors,
    IHostContributor[] HostContributors,
    IAppRegistrationContributor[] AppRegistrationContributors,
    IDebugManifestContributor[] DebugManifestContributors)
{
    /// <inheritdoc />
    public bool Equals(BlueprintModuleExecutionPlan? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return ContributorsEqual(this.PostInitializationContributors, other.PostInitializationContributors)
            && ContributorsEqual(this.DescriptorContributors, other.DescriptorContributors)
            && ContributorsEqual(this.ExceptionTranslationContributors, other.ExceptionTranslationContributors)
            && ContributorsEqual(this.PipelineContributors, other.PipelineContributors)
            && ContributorsEqual(this.HostContributors, other.HostContributors)
            && ContributorsEqual(this.AppRegistrationContributors, other.AppRegistrationContributors)
            && ContributorsEqual(this.DebugManifestContributors, other.DebugManifestContributors);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hashCode = default;

        AddContributors(ref hashCode, this.PostInitializationContributors);
        AddContributors(ref hashCode, this.DescriptorContributors);
        AddContributors(ref hashCode, this.ExceptionTranslationContributors);
        AddContributors(ref hashCode, this.PipelineContributors);
        AddContributors(ref hashCode, this.HostContributors);
        AddContributors(ref hashCode, this.AppRegistrationContributors);
        AddContributors(ref hashCode, this.DebugManifestContributors);

        return hashCode.ToHashCode();
    }

    private static bool ContributorsEqual<TContributor>(TContributor[] left, TContributor[] right)
        where TContributor : IBlueprintContributor
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!ContributorEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContributorEquals(IBlueprintContributor left, IBlueprintContributor right)
    {
        return left.GetType() == right.GetType()
            && StringComparer.Ordinal.Equals(left.ContributorId, right.ContributorId)
            && left.Order == right.Order
            && left.IsUnique == right.IsUnique
            && left.DependsOn.SequenceEqual(right.DependsOn, StringComparer.Ordinal);
    }

    private static void AddContributors<TContributor>(ref HashCode hashCode, TContributor[] contributors)
        where TContributor : IBlueprintContributor
    {
        foreach (var contributor in contributors)
        {
            hashCode.Add(contributor.GetType());
            hashCode.Add(contributor.ContributorId, StringComparer.Ordinal);
            hashCode.Add(contributor.Order);
            hashCode.Add(contributor.IsUnique);

            foreach (var dependency in contributor.DependsOn)
            {
                hashCode.Add(dependency, StringComparer.Ordinal);
            }
        }
    }
}
