using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions;

// Types whose schema names collide, as Signature365's Generation app has them: a type and its "...ApiResource" twin that
// Replace("ApiResource", "") maps together, and two types of one simple name. Each carries a property no other has, so a
// reference shows which schema it lands on. The operations are ordered so that neither the types' own names nor the names
// of the roots that reach them put the colliding types in the order the document reaches them in.

/// <summary>The "...ApiResource" twin of the two <c>CollisionItem</c>s.</summary>
public sealed class CollisionItemApiResource
{
    public string? OnlyOnResource { get; init; }
}

/// <summary>Holds the first of two types named <c>CollisionItem</c>.</summary>
public static class CollisionOne
{
    /// <summary>The first of two types named <c>CollisionItem</c>.</summary>
    public sealed class CollisionItem
    {
        public string? OnlyOnOne { get; init; }
    }
}

/// <summary>Holds the second of two types named <c>CollisionItem</c>.</summary>
public static class CollisionTwo
{
    /// <summary>The second of two types named <c>CollisionItem</c>.</summary>
    public sealed class CollisionItem
    {
        public string? OnlyOnTwo { get; init; }
    }
}

/// <summary>An enum that shares its name with <see cref="CollisionKindApiResource"/> once "ApiResource" is removed.</summary>
public enum CollisionKind
{
    Plain,
    Fancy,
}

/// <summary>An object that shares its name with <see cref="CollisionKind"/> once "ApiResource" is removed.</summary>
public sealed class CollisionKindApiResource
{
    public string? OnlyOnKindResource { get; init; }
}

/// <summary>Holds the first of two enums named <c>CollisionStatus</c>, a distinct simple-name clash between two enums
/// rather than between an enum and the object its formatter maps to it (<see cref="CollisionKind"/>).</summary>
public static class CollisionStatusOne
{
    /// <summary>The first of two enums named <c>CollisionStatus</c>.</summary>
    public enum CollisionStatus
    {
        Draft,
        Live,
    }
}

/// <summary>Holds the second of two enums named <c>CollisionStatus</c>.</summary>
public static class CollisionStatusTwo
{
    /// <summary>The second of two enums named <c>CollisionStatus</c>, with a value set of its own.</summary>
    public enum CollisionStatus
    {
        Archived,
        Active,
        Pending,
    }
}

/// <summary>Reaches every colliding item — directly, through a collection's items and a dictionary's values.</summary>
/// <remarks>Declared in alphabetical order, so the order legacy walked them in is the order the document writes them in.</remarks>
public sealed class CollisionHolderApiResource
{
    public CollisionItemApiResource Alpha { get; init; } = new();

    public CollisionOne.CollisionItem Bravo { get; init; } = new();

    public CollisionTwo.CollisionItem Charlie { get; init; } = new();

    public List<CollisionTwo.CollisionItem> Delta { get; init; } = [];

    public Dictionary<string, CollisionOne.CollisionItem> Echo { get; init; } = [];

    public CollisionKind Foxtrot { get; init; }

    public CollisionKindApiResource Golf { get; init; } = new();

    public CollisionStatusOne.CollisionStatus Hotel { get; init; }

    public CollisionStatusTwo.CollisionStatus India { get; init; }
}

/// <summary>The "...ApiResource" twin of <see cref="CollisionPortal.CollisionMatch"/>, reached by the earlier operation.</summary>
public sealed class CollisionMatchApiResource
{
    public string? OnlyOnMatchResource { get; init; }
}

/// <summary>Holds the <c>CollisionMatch</c> a later operation reaches, as Signature365's Portal contract has its own.</summary>
public static class CollisionPortal
{
    /// <summary>The <c>CollisionMatch</c> the later operation reaches.</summary>
    public sealed class CollisionMatch
    {
        public string? OnlyOnPortal { get; init; }
    }
}

/// <summary>A log that reaches <see cref="CollisionMatchApiResource"/>, as Generation's log reaches its rule match.</summary>
public sealed class CollisionTrailApiResource
{
    public CollisionMatchApiResource Match { get; init; } = new();
}

/// <summary>A preview that reaches <see cref="CollisionPortal.CollisionMatch"/>, as Generation's preview reaches Portal's.</summary>
public sealed class CollisionPreviewApiResource
{
    public CollisionPortal.CollisionMatch Match { get; init; } = new();
}

/// <summary>Gets the holder, the first operation to reach a <c>CollisionItem</c>.</summary>
[AllowAnonymous]
[RootLink("/collisions/holder")]
public partial class GetCollisionHolderQuery : IQuery<CollisionHolderApiResource>
{
    public static CollisionHolderApiResource Handle(GetCollisionHolderQuery query)
    {
        return new CollisionHolderApiResource();
    }
}

/// <summary>Gets a log, the first operation to reach a <c>CollisionMatch</c>.</summary>
[AllowAnonymous]
[RootLink("/collisions/logs")]
public partial class GetCollisionLogQuery : IQuery<CollisionTrailApiResource>
{
    public static CollisionTrailApiResource Handle(GetCollisionLogQuery query)
    {
        return new CollisionTrailApiResource();
    }
}

/// <summary>Returns one of the colliding items as its response, after another has taken the name.</summary>
[AllowAnonymous]
[RootLink("/collisions/one")]
public partial class GetCollisionOneQuery : IQuery<CollisionOne.CollisionItem>
{
    public static CollisionOne.CollisionItem Handle(GetCollisionOneQuery query)
    {
        return new CollisionOne.CollisionItem();
    }
}

/// <summary>Returns the other colliding item as its response, so that whichever of the two is named apart is returned.</summary>
[AllowAnonymous]
[RootLink("/collisions/two")]
public partial class GetCollisionTwoQuery : IQuery<CollisionTwo.CollisionItem>
{
    public static CollisionTwo.CollisionItem Handle(GetCollisionTwoQuery query)
    {
        return new CollisionTwo.CollisionItem();
    }
}

/// <summary>Gets a preview, the second operation to reach a <c>CollisionMatch</c>.</summary>
[AllowAnonymous]
[RootLink("/collisions/previews")]
public partial class GetCollisionPreviewQuery : IQuery<CollisionPreviewApiResource>
{
    public static CollisionPreviewApiResource Handle(GetCollisionPreviewQuery query)
    {
        return new CollisionPreviewApiResource();
    }
}

/// <summary>Holds the first of two <c>ApiResource</c>s named <c>EnvelopeMatch</c>, wrapped in a ResourceEvent envelope
/// below (R28).</summary>
public static class EnvelopeOne
{
    /// <summary>The first of two <c>EnvelopeMatch</c> resources.</summary>
    public sealed class EnvelopeMatch : ApiResource
    {
        public override string Object => "EnvelopeMatchOne";
    }
}

/// <summary>Holds the second of two <c>ApiResource</c>s named <c>EnvelopeMatch</c>.</summary>
public static class EnvelopeTwo
{
    /// <summary>The second of two <c>EnvelopeMatch</c> resources.</summary>
    public sealed class EnvelopeMatch : ApiResource
    {
        public override string Object => "EnvelopeMatchTwo";
    }
}

/// <summary>Holds the third <c>ApiResource</c> named <c>EnvelopeMatch</c>, returned plain (not in an envelope) by the
/// operation that sorts first (R135).</summary>
public static class EnvelopeThree
{
    /// <summary>The third <c>EnvelopeMatch</c> resource.</summary>
    public sealed class EnvelopeMatch : ApiResource
    {
        public override string Object => "EnvelopeMatchThree";
    }
}

/// <summary>
/// Gets the third resource plain, at a URL that sorts before both envelope operations', so the document reaches it
/// first and gives it the base key <c>EnvelopeMatch</c>. Each envelope's inner type is then already past its
/// projection-time name when its envelope is reached: an envelope key built from the inner's base name without first
/// reaching the inner would be <c>ResourceEventOfEnvelopeMatch</c> for a type that is finally <c>EnvelopeMatch2</c>.
/// </summary>
[AllowAnonymous]
[RootLink("/collisions/envelopes/000-three")]
public partial class GetEnvelopeThreeQuery : IQuery<EnvelopeThree.EnvelopeMatch>
{
    public static EnvelopeThree.EnvelopeMatch Handle(GetEnvelopeThreeQuery query)
    {
        return new EnvelopeThree.EnvelopeMatch();
    }
}

/// <summary>
/// Submits the second-projected resource (its namespace, <c>EnvelopeTwo</c>, sorts after <c>EnvelopeOne</c>'s — so the
/// schema projection's own walk names it <c>EnvelopeMatch2</c> — but at a URL that sorts first, so the document's reach
/// order gives it the un-suffixed final key <c>EnvelopeMatch</c>: projection-time name and final key disagree, which is
/// R28's bug — the envelope must be named from the final key (<c>ResourceEventOfEnvelopeMatch</c>), not the
/// projection-time one (<c>ResourceEventOfEnvelopeMatch2</c>).
/// </summary>
[AllowAnonymous]
[RootLink("/collisions/envelopes/aaa-two")]
[HttpPost]
public partial class SubmitEnvelopeTwoCommand : ICommand<ResourceEvent<EnvelopeTwo.EnvelopeMatch>>
{
    public static ResourceEvent<EnvelopeTwo.EnvelopeMatch> Handle(SubmitEnvelopeTwoCommand command)
    {
        return new ResourceEvent<EnvelopeTwo.EnvelopeMatch>(ResourceEventChangeType.Created, "created", new EnvelopeTwo.EnvelopeMatch());
    }
}

/// <summary>Submits the first-projected resource (kept the base name <c>EnvelopeMatch</c> in the schema projection's
/// own walk) at a URL reached after the one above, so the document's reach order gives it the final key
/// <c>EnvelopeMatch2</c> instead — the swap this fixture is for.</summary>
[AllowAnonymous]
[RootLink("/collisions/envelopes/zzz-one")]
[HttpPost]
public partial class SubmitEnvelopeOneCommand : ICommand<ResourceEvent<EnvelopeOne.EnvelopeMatch>>
{
    public static ResourceEvent<EnvelopeOne.EnvelopeMatch> Handle(SubmitEnvelopeOneCommand command)
    {
        return new ResourceEvent<EnvelopeOne.EnvelopeMatch>(ResourceEventChangeType.Created, "created", new EnvelopeOne.EnvelopeMatch());
    }
}

/// <summary>Submits one of the colliding items in its body.</summary>
[AllowAnonymous]
[RootLink("/collisions/submit")]
[HttpPost]
public partial class SubmitCollisionCommand : ICommand<CollisionHolderApiResource>
{
    public CollisionTwo.CollisionItem? Item { get; set; }

    public static CollisionHolderApiResource Handle(SubmitCollisionCommand command)
    {
        return new CollisionHolderApiResource();
    }
}
