using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Sets the notification priority for a template, and optionally attaches a notification rule to it.
/// Demonstrates two shapes not otherwise exercised by a request body in this reference app:
/// <list type="bullet">
///   <item>
///   an integer-typed <see cref="RequiredIfNotAttribute"/> dependent (<see cref="Level"/>), so the projected
///   document's declared <c>type: "integer"</c> and its conditional's numeric <c>const</c> must agree in one
///   emitted document — a string <c>const</c> against an integer property could never match, and under the
///   <c>not</c> wrapper that makes <c>then</c> always apply.
///   </item>
///   <item>
///   a body property (<see cref="Rule"/>) whose CLR type is the same conditional-bearing nested DTO
///   (<see cref="TemplateNotificationRuleApiResource"/>) that <see cref="GetTemplateNotificationRulesQuery"/>
///   reaches from a response. A request-body property never emits a <c>$ref</c> for a complex type — it
///   renders inline <c>type: "object"</c> instead — so the DTO's own conditional cannot reach this component
///   by that route, and this operation exists to prove exactly that limitation in one document.
///   </item>
/// </list>
/// </summary>
[ApiOperations("Templates")]
[Link(typeof(TemplateApiResource), "/templates/{Id}/notification-priority", Rel = "set-notification-priority")]
[HttpPut]
[Resource("template", "write")]
[Description("Set the notification priority for a template, and optionally attach a notification rule.")]
public partial class SetTemplateNotificationPriorityCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>
    /// The identifier of the template to update (bound from the route).
    /// </summary>
    [Description("The unique identifier of the template.")]
    public required string Id { get; set; }

    /// <summary>
    /// The notification priority level. <see cref="Reason"/> becomes required unless this equals 5
    /// (via <see cref="RequiredIfNotAttribute"/>). 5, not the CLR default of 0, is deliberately the level that
    /// suppresses the requirement, so a projection bug that fell back to the default int value would show up
    /// as a spurious pass here.
    /// </summary>
    [Description("The notification priority level. Reason is required unless this is 5.")]
    public int Level { get; set; }

    /// <summary>
    /// The reason for the priority change. Required unless <see cref="Level"/> is 5.
    /// </summary>
    [RequiredIfNot(nameof(Level), 5)]
    [Description("Reason for the priority change. Required unless Level is 5.")]
    public string? Reason { get; set; }

    /// <summary>
    /// An optional notification rule to attach to the template. Reaches the document only as an inline,
    /// request-body-only property — never a <c>$ref</c> — so any conditional it declares belongs solely to
    /// the <see cref="TemplateNotificationRuleApiResource"/> component built from serializer metadata for the
    /// response side, not to this operation's body.
    /// </summary>
    [Description("An optional notification rule to attach to the template.")]
    public TemplateNotificationRuleApiResource? Rule { get; set; }
}
