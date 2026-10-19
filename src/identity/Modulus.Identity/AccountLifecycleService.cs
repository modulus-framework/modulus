using Microsoft.AspNetCore.Identity;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Security;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity;

/// <summary>
/// The explicit lifecycle API. It validates the move, sets the suspension fields, and saves through the user manager, which writes the
/// transition row with the actor and reason given here and notifies access observers.
/// </summary>
internal sealed class AccountLifecycleService<TUser>(UserManager<TUser> users, ISecurityAuditLog? audit = null) : IAccountLifecycleService
    where TUser : ModulusUser
{
    private const int MaxReasonLength = 64;
    private const int MaxNoteLength = 512;

    public async Task<AccountLifecycle> ChangeAsync(AccountLifecycleChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var user = await users.FindByIdAsync(change.UserId.ToString())
            ?? throw new NotFoundException("No account has that identifier.");

        var from = user.Lifecycle;
        if (from == change.To)
            return from;

        // Throws ConflictException for a move the matrix does not allow, before anything is changed.
        _ = AccountLifecycleRules.Transition(from, change.To, change.RehireReviewed);

        if (change.To == AccountLifecycle.Disabled)
        {
            var reason = Reason(change.Reason);
            var note = Note(change.Note);
            if (SuspensionReasons.RequiresNote(reason) && note is null)
                throw new ArgumentException("This suspension reason needs a note.", nameof(change));
            user.SuspensionReason = reason;
            user.SuspensionNote = note;
        }
        else if (from == AccountLifecycle.Disabled)
        {
            // Reinstated: the suspension no longer applies.
            user.SuspensionReason = null;
            user.SuspensionNote = null;
        }

        user.Lifecycle = change.To;
        using var scope = AccountLifecycleScope.Enter(change.ActorId, Clean(change.Reason), change.RehireReviewed);
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new ConflictException(string.Join(" ", result.Errors.Select(e => e.Description)));

        audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = "account.lifecycle_changed",
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = user.TenantId,
            Actor = change.ActorId?.ToString(),
            Target = $"user:{user.Id}",
            Details = new Dictionary<string, string?>
            {
                ["from"] = from.ToString(),
                ["to"] = change.To.ToString(),
                ["reason"] = Clean(change.Reason),
            },
        });
        return change.To;
    }

    private static string Reason(string? reason)
    {
        var value = Clean(reason);
        if (value is null)
            throw new ArgumentException("Disabling an account needs a suspension reason.", "Reason");
        if (value.Length > MaxReasonLength)
            throw new ArgumentException($"A suspension reason is at most {MaxReasonLength} characters.", "Reason");
        return value;
    }

    private static string? Note(string? note)
    {
        var value = Clean(note);
        if (value is not null && value.Length > MaxNoteLength)
            throw new ArgumentException($"A suspension note is at most {MaxNoteLength} characters.", "Note");
        return value;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
