using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Answers <see cref="AddTenantNoteCommand"/> with the note exactly as it arrived, so a caller can see what a gateway sent.
/// </summary>
public class AddTenantNoteCommandHandler
{
    /// <summary>
    /// Echoes the command.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The note as received.</returns>
    public TenantNoteApiResource Handle(AddTenantNoteCommand command)
    {
        return new TenantNoteApiResource
        {
            TenantId = command.TenantId,
            Text = command.Text,
            Source = command.Source,
            InternalNote = command.InternalNote,
        };
    }
}
