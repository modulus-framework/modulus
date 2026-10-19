using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Identity.Abstractions;

namespace Modulus.Identity.Scim;

/// <summary>The SCIM operations over the Identity user and role managers, in the current company.</summary>
internal interface IScimService
{
    Task<ScimListResponse<ScimUser>> ListUsersAsync(string? filter, int startIndex, int count, string baseUrl, CancellationToken ct);

    Task<ScimUser?> GetUserAsync(string id, string baseUrl, CancellationToken ct);

    Task<ScimUser> CreateUserAsync(ScimUser model, string baseUrl, CancellationToken ct);

    Task<ScimUser?> ReplaceUserAsync(string id, ScimUser model, string baseUrl, CancellationToken ct);

    Task<ScimUser?> PatchUserAsync(string id, ScimPatchRequest patch, string baseUrl, CancellationToken ct);

    Task<bool> DeleteUserAsync(string id, CancellationToken ct);

    Task<ScimListResponse<ScimGroup>> ListGroupsAsync(string? filter, int startIndex, int count, string baseUrl, CancellationToken ct);

    Task<ScimGroup?> GetGroupAsync(string id, string baseUrl, CancellationToken ct);

    Task<ScimGroup> CreateGroupAsync(ScimGroup model, string baseUrl, CancellationToken ct);

    Task<ScimGroup?> ReplaceGroupAsync(string id, ScimGroup model, string baseUrl, CancellationToken ct);

    Task<ScimGroup?> PatchGroupAsync(string id, ScimPatchRequest patch, string baseUrl, CancellationToken ct);

    Task<bool> DeleteGroupAsync(string id, CancellationToken ct);
}

internal sealed class ScimService<TUser, TRole>(
    UserManager<TUser> users,
    RoleManager<TRole> roles,
    ICurrentTenant tenant,
    IOptions<ScimOptions> options,
    IServiceProvider services,
    ILogger<ScimService<TUser, TRole>> logger) : IScimService
    where TUser : ModulusUser, new()
    where TRole : ModulusRole, new()
{
    private static readonly IReadOnlyDictionary<string, ScimAttribute<TUser>> s_userAttributes =
        new Dictionary<string, ScimAttribute<TUser>>(StringComparer.Ordinal)
        {
            ["id"] = new(ScimAttributeKind.Guid, u => u.Id),
            ["username"] = new(ScimAttributeKind.String, u => u.UserName),
            ["externalid"] = new(ScimAttributeKind.String, u => u.ExternalId),
            ["emails"] = new(ScimAttributeKind.String, u => u.Email),
            ["emails.value"] = new(ScimAttributeKind.String, u => u.Email),
            ["name.givenname"] = new(ScimAttributeKind.String, u => u.FirstName),
            ["name.familyname"] = new(ScimAttributeKind.String, u => u.LastName),
            // Inline, not MayStartNewAccess(): this getter is translated into the SQL of a filter.
            ["active"] = new(ScimAttributeKind.Boolean, u => u.IsActive && u.Lifecycle == AccountLifecycle.Active),
        };

    private static readonly IReadOnlyDictionary<string, ScimAttribute<TRole>> s_groupAttributes =
        new Dictionary<string, ScimAttribute<TRole>>(StringComparer.Ordinal)
        {
            ["id"] = new(ScimAttributeKind.Guid, r => r.Id),
            ["displayname"] = new(ScimAttributeKind.String, r => r.Name),
        };

    private ScimOptions Options => options.Value;

    // ── Users ──────────────────────────────────────────────────────

    public async Task<ScimListResponse<ScimUser>> ListUsersAsync(string? filter, int startIndex, int count, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var query = users.Users.AsNoTracking().Where(u => u.TenantId == company);
        if (ScimFilter.Parse(filter, s_userAttributes) is { } predicate)
            query = query.Where(predicate);

        var (skip, take) = Page(startIndex, count);
        var total = await query.CountAsync(ct);
        var page = take == 0 ? [] : await query.OrderBy(u => u.Id).Skip(skip).Take(take).ToListAsync(ct);
        return new ScimListResponse<ScimUser>
        {
            TotalResults = total,
            StartIndex = skip + 1,
            ItemsPerPage = page.Count,
            Resources = [.. page.Select(u => ToScim(u, null, baseUrl))],
        };
    }

    public async Task<ScimUser?> GetUserAsync(string id, string baseUrl, CancellationToken ct)
    {
        var user = await FindUserAsync(id);
        return user is null ? null : await ToScimWithGroupsAsync(user, baseUrl);
    }

    public async Task<ScimUser> CreateUserAsync(ScimUser model, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var userName = model.UserName?.Trim();
        if (string.IsNullOrEmpty(userName))
            throw new ScimException(400, "invalidValue", "userName is required.");

        var user = new TUser { TenantId = company, EmailConfirmed = true };
        var active = model.Active ?? true;
        await ApplyAsync(user, model, create: true, company, ct);
        user.IsActive = active;

        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
            throw FromIdentity(created);

        if (active)
        {
            try
            {
                await AddMemberAsync(user.Id, company, ct);
            }
            catch (ScimException)
            {
                await users.DeleteAsync(user);
                throw;
            }
        }

        if (Options.SendInvitation && services.GetService<IIdentityEmailSender>() is { } sender && user.Email is { } email)
            await sender.SendInvitationEmailAsync(email, await users.GeneratePasswordResetTokenAsync(user), ct);

        Audit("scim.user.created", user);
        return await ToScimWithGroupsAsync(user, baseUrl);
    }

    public async Task<ScimUser?> ReplaceUserAsync(string id, ScimUser model, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var user = await FindUserAsync(id);
        if (user is null)
            return null;
        if (string.IsNullOrWhiteSpace(model.UserName))
            throw new ScimException(400, "invalidValue", "userName is required.");

        model.Active ??= user.MayStartNewAccess();
        await UpdateAsync(user, model, company, ct);
        return await ToScimWithGroupsAsync(user, baseUrl);
    }

    public async Task<ScimUser?> PatchUserAsync(string id, ScimPatchRequest patch, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var user = await FindUserAsync(id);
        if (user is null)
            return null;

        var draft = ToScim(user, null, baseUrl);
        foreach (var op in Operations(patch))
            ApplyUserOperation(draft, op);

        await UpdateAsync(user, draft, company, ct);
        return await ToScimWithGroupsAsync(user, baseUrl);
    }

    public async Task<bool> DeleteUserAsync(string id, CancellationToken ct)
    {
        var company = RequireCompany();
        var user = await FindUserAsync(id);
        if (user is null)
            return false;

        await RemoveMemberAsync(user.Id, company, ct);
        if (Options.DeleteRemovesAccount)
        {
            var deleted = await users.DeleteAsync(user);
            if (!deleted.Succeeded)
                throw FromIdentity(deleted);
            Audit("scim.user.deleted", user);
        }
        else if (user.MayStartNewAccess())
        {
            user.IsActive = false;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
                throw FromIdentity(updated);
            Audit("scim.user.deactivated", user);
        }

        return true;
    }

    private async Task UpdateAsync(TUser user, ScimUser model, Guid company, CancellationToken ct)
    {
        var wasActive = user.MayStartNewAccess();
        var active = model.Active ?? wasActive;
        await ApplyAsync(user, model, create: false, company, ct);

        // Taking a seat can be refused, so do it before the account changes.
        if (active && !wasActive)
            await AddMemberAsync(user.Id, company, ct);

        user.IsActive = active;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
            throw FromIdentity(updated);

        if (!active && wasActive)
            await RemoveMemberAsync(user.Id, company, ct);

        Audit(active ? "scim.user.updated" : wasActive ? "scim.user.deactivated" : "scim.user.updated", user);
    }

    /// <summary>Copies the writable attributes of <paramref name="model"/> onto <paramref name="user"/> (not saved).</summary>
    private async Task ApplyAsync(TUser user, ScimUser model, bool create, Guid company, CancellationToken ct)
    {
        var userName = model.UserName?.Trim();
        if (!string.IsNullOrEmpty(userName))
            user.UserName = userName;

        var email = (model.Emails?.FirstOrDefault(e => e.Primary == true) ?? model.Emails?.FirstOrDefault())?.Value?.Trim();
        email ??= userName is not null && userName.Contains('@', StringComparison.Ordinal) ? userName : null;
        if (!string.IsNullOrEmpty(email))
            user.Email = email;
        else if (create || string.IsNullOrEmpty(user.Email))
            throw new ScimException(400, "invalidValue", "An e-mail address is required (emails, or a userName that is one).");

        user.FirstName = model.Name?.GivenName;
        user.LastName = model.Name?.FamilyName;

        var externalId = string.IsNullOrWhiteSpace(model.ExternalId) ? null : model.ExternalId.Trim();
        if (externalId is not null
            && await users.Users.AnyAsync(u => u.TenantId == company && u.ExternalId == externalId && u.Id != user.Id, ct))
            throw new ScimException(409, "uniqueness", "Another account already has this externalId.");
        user.ExternalId = externalId;
    }

    private async Task<TUser?> FindUserAsync(string id)
    {
        var company = RequireCompany();
        if (!Guid.TryParse(id, CultureInfo.InvariantCulture, out var guid))
            return null;
        var user = await users.FindByIdAsync(guid.ToString());
        return user is not null && user.TenantId == company ? user : null;
    }

    private ScimUser ToScim(TUser user, IEnumerable<ScimRef>? groups, string baseUrl)
    {
        var name = new ScimName { GivenName = user.FirstName, FamilyName = user.LastName };
        var formatted = $"{user.FirstName} {user.LastName}".Trim();
        name.Formatted = formatted.Length > 0 ? formatted : null;
        return new ScimUser
        {
            Id = user.Id.ToString(),
            ExternalId = user.ExternalId,
            UserName = user.UserName,
            Name = name,
            DisplayName = formatted.Length > 0 ? formatted : user.UserName,
            Emails = user.Email is null ? [] : [new ScimEmail { Value = user.Email, Type = "work", Primary = true }],
            Active = user.MayStartNewAccess(),
            Groups = groups?.ToList(),
            Meta = new ScimMeta { ResourceType = "User", Location = $"{baseUrl}/Users/{user.Id}" },
        };
    }

    private async Task<ScimUser> ToScimWithGroupsAsync(TUser user, string baseUrl)
    {
        var names = await users.GetRolesAsync(user);
        var visible = await ManagedRoles().Where(r => names.Contains(r.Name!)).ToListAsync();
        return ToScim(user, visible.Select(r => new ScimRef
        {
            Value = r.Id.ToString(),
            Display = r.Name,
            Type = "Group",
            Ref = $"{baseUrl}/Groups/{r.Id}",
        }), baseUrl);
    }

    private void ApplyUserOperation(ScimUser draft, ScimPatchOperation operation)
    {
        var op = OperationName(operation);
        var path = operation.Path?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            if (operation.Value is not { ValueKind: JsonValueKind.Object } bag)
                throw new ScimException(400, "invalidValue", "An operation without a path needs an object of attributes.");
            foreach (var property in bag.EnumerateObject())
                ApplyUserAttribute(draft, op, property.Name, property.Value);
            return;
        }

        ApplyUserAttribute(draft, op, path, operation.Value);
    }

    private static void ApplyUserAttribute(ScimUser draft, string op, string path, JsonElement? value)
    {
        var key = path.ToLowerInvariant();
        const string prefix = "urn:ietf:params:scim:schemas:core:2.0:user:";
        if (key.StartsWith(prefix, StringComparison.Ordinal))
            key = key[prefix.Length..];
        var remove = op == "remove";

        switch (key)
        {
            case "username":
                draft.UserName = remove ? null : RequireText(value, "userName");
                break;
            case "externalid":
                draft.ExternalId = remove ? null : RequireText(value, "externalId");
                break;
            case "active":
                draft.Active = remove ? false : RequireBool(value, "active");
                break;
            case "name":
                draft.Name = remove
                    ? new ScimName()
                    : new ScimName
                    {
                        GivenName = StringProperty(value, "givenName") ?? draft.Name?.GivenName,
                        FamilyName = StringProperty(value, "familyName") ?? draft.Name?.FamilyName,
                    };
                break;
            case "name.givenname":
                draft.Name ??= new ScimName();
                draft.Name.GivenName = remove ? null : RequireText(value, "name.givenName");
                break;
            case "name.familyname":
                draft.Name ??= new ScimName();
                draft.Name.FamilyName = remove ? null : RequireText(value, "name.familyName");
                break;
            case "id" or "groups" or "meta":
                throw new ScimException(400, "mutability", $"'{path}' is read-only.");
            default:
                if (key.StartsWith("emails", StringComparison.Ordinal))
                {
                    if (remove)
                        throw new ScimException(400, "invalidValue", "The e-mail address cannot be removed.");
                    var address = EmailFrom(value);
                    if (address is not null)
                        draft.Emails = [new ScimEmail { Value = address, Type = "work", Primary = true }];
                }

                // Attributes this server does not keep (displayName, title, phone numbers, extensions) are accepted and ignored,
                // so an identity provider that sends its full profile still syncs.
                break;
        }
    }

    private static string? EmailFrom(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.String } v => v.GetString(),
        { ValueKind: JsonValueKind.Array } v => v.EnumerateArray().Select(AddressOf).FirstOrDefault(a => a is not null),
        { ValueKind: JsonValueKind.Object } v => AddressOf(v),
        _ => null,
    };

    private static string? AddressOf(JsonElement element)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // ── Groups (roles) ─────────────────────────────────────────────

    public async Task<ScimListResponse<ScimGroup>> ListGroupsAsync(string? filter, int startIndex, int count, string baseUrl, CancellationToken ct)
    {
        RequireCompany();
        var query = ManagedRoles().AsNoTracking();
        if (ScimFilter.Parse(filter, s_groupAttributes) is { } predicate)
            query = query.Where(predicate);

        var (skip, take) = Page(startIndex, count);
        var total = await query.CountAsync(ct);
        var page = take == 0 ? [] : await query.OrderBy(r => r.Id).Skip(skip).Take(take).ToListAsync(ct);
        var resources = new List<ScimGroup>();
        foreach (var role in page)
            resources.Add(await ToGroupAsync(role, baseUrl));
        return new ScimListResponse<ScimGroup>
        {
            TotalResults = total,
            StartIndex = skip + 1,
            ItemsPerPage = resources.Count,
            Resources = resources,
        };
    }

    public async Task<ScimGroup?> GetGroupAsync(string id, string baseUrl, CancellationToken ct)
    {
        var role = await FindRoleAsync(id);
        return role is null ? null : await ToGroupAsync(role, baseUrl);
    }

    public async Task<ScimGroup> CreateGroupAsync(ScimGroup model, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var name = model.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ScimException(400, "invalidValue", "displayName is required.");
        if (!Options.AllowRoleCreation || !IsManaged(name))
            throw new ScimException(403, null, "This server does not let a provisioning client create this group.");

        var members = await ResolveMembersAsync(model.Members);
        var created = await roles.CreateAsync(new TRole { Name = name, DisplayName = name, TenantId = company });
        if (!created.Succeeded)
            throw FromIdentity(created);

        var role = (await roles.FindByNameAsync(name))!;
        await SetMembersAsync(role, members, company);
        Audit("scim.group.created", role);
        await NotifyRoleChangedAsync(role, "role.created", company);
        return await ToGroupAsync(role, baseUrl);
    }

    public async Task<ScimGroup?> ReplaceGroupAsync(string id, ScimGroup model, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var role = await FindRoleAsync(id);
        if (role is null)
            return null;

        RequireSameName(role, model.DisplayName);
        await SetMembersAsync(role, await ResolveMembersAsync(model.Members), company);
        return await ToGroupAsync(role, baseUrl);
    }

    public async Task<ScimGroup?> PatchGroupAsync(string id, ScimPatchRequest patch, string baseUrl, CancellationToken ct)
    {
        var company = RequireCompany();
        var role = await FindRoleAsync(id);
        if (role is null)
            return null;

        var members = (await users.GetUsersInRoleAsync(role.Name!)).Select(u => u.Id).ToHashSet();
        foreach (var operation in Operations(patch))
            await ApplyGroupOperationAsync(role, members, operation);

        await SetMembersAsync(role, members, company);
        return await ToGroupAsync(role, baseUrl);
    }

    public async Task<bool> DeleteGroupAsync(string id, CancellationToken ct)
    {
        var company = RequireCompany();
        var role = await FindRoleAsync(id);
        if (role is null)
            return false;
        if (!Options.AllowRoleCreation)
            throw new ScimException(403, null, "This server does not let a provisioning client delete groups.");

        var deleted = await roles.DeleteAsync(role);
        if (!deleted.Succeeded)
            throw FromIdentity(deleted);
        Audit("scim.group.deleted", role);
        await NotifyRoleChangedAsync(role, "role.deleted", company);
        return true;
    }

    private async Task ApplyGroupOperationAsync(TRole role, HashSet<Guid> members, ScimPatchOperation operation)
    {
        var op = OperationName(operation);
        var path = operation.Path?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            if (operation.Value is not { ValueKind: JsonValueKind.Object } bag)
                throw new ScimException(400, "invalidValue", "An operation without a path needs an object of attributes.");
            foreach (var property in bag.EnumerateObject())
                await ApplyGroupAttributeAsync(role, members, op, property.Name, property.Value);
            return;
        }

        await ApplyGroupAttributeAsync(role, members, op, path, operation.Value);
    }

    private async Task ApplyGroupAttributeAsync(TRole role, HashSet<Guid> members, string op, string path, JsonElement? value)
    {
        var key = path.ToLowerInvariant();
        if (key == "displayname")
        {
            if (op != "remove")
                RequireSameName(role, RequireText(value, "displayName"));
            return;
        }

        if (key == "members")
        {
            var ids = await ResolveMembersAsync(ReferencesFrom(value));
            switch (op)
            {
                case "add":
                    members.UnionWith(ids);
                    break;
                case "replace":
                    members.Clear();
                    members.UnionWith(ids);
                    break;
                default:
                    if (value is null)
                        members.Clear();
                    else
                        members.ExceptWith(ids);
                    break;
            }

            return;
        }

        // members[value eq "id"] removes one member.
        if (key.StartsWith("members[", StringComparison.Ordinal) && op == "remove")
        {
            var open = path.IndexOf('"', StringComparison.Ordinal);
            var close = path.LastIndexOf('"');
            if (open < 0 || close <= open || !Guid.TryParse(path.AsSpan(open + 1, close - open - 1), CultureInfo.InvariantCulture, out var member))
                throw new ScimException(400, "invalidPath", "Expected members[value eq \"<id>\"].");
            members.Remove(member);
            return;
        }

        if (key is "id" or "meta")
            throw new ScimException(400, "mutability", $"'{path}' is read-only.");
        // Other attributes (externalId) are accepted and ignored.
    }

    private static List<ScimRef>? ReferencesFrom(JsonElement? value)
    {
        if (value is not { } v)
            return null;
        var list = new List<ScimRef>();
        var items = v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [v];
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var id) && id.ValueKind == JsonValueKind.String)
                list.Add(new ScimRef { Value = id.GetString() });
            else
                throw new ScimException(400, "invalidValue", "Members are objects with a 'value' (the user id).");
        }

        return list;
    }

    private async Task<HashSet<Guid>> ResolveMembersAsync(List<ScimRef>? references)
    {
        var ids = new HashSet<Guid>();
        foreach (var reference in references ?? [])
        {
            if (!Guid.TryParse(reference.Value, CultureInfo.InvariantCulture, out var id) || await FindUserAsync(id.ToString()) is null)
                throw new ScimException(400, "invalidValue", $"The member '{reference.Value}' is not an account of this company.");
            ids.Add(id);
        }

        return ids;
    }

    /// <summary>Makes the role's members exactly <paramref name="wanted"/>.</summary>
    private async Task SetMembersAsync(TRole role, HashSet<Guid> wanted, Guid company)
    {
        var current = (await users.GetUsersInRoleAsync(role.Name!)).ToDictionary(u => u.Id);
        var changed = false;
        foreach (var id in wanted.Where(id => !current.ContainsKey(id)))
        {
            var added = await users.AddToRoleAsync((await FindUserAsync(id.ToString()))!, role.Name!);
            if (!added.Succeeded)
                throw FromIdentity(added);
            changed = true;
        }

        foreach (var user in current.Values.Where(u => !wanted.Contains(u.Id)))
        {
            var removed = await users.RemoveFromRoleAsync(user, role.Name!);
            if (!removed.Succeeded)
                throw FromIdentity(removed);
            changed = true;
        }

        if (changed)
            Audit("scim.group.members-changed", role);
        _ = company;
    }

    private async Task<ScimGroup> ToGroupAsync(TRole role, string baseUrl)
    {
        var members = await users.GetUsersInRoleAsync(role.Name!);
        return new ScimGroup
        {
            Id = role.Id.ToString(),
            DisplayName = role.Name,
            Members = [.. members.OrderBy(u => u.Id).Select(u => new ScimRef
            {
                Value = u.Id.ToString(),
                Display = u.UserName,
                Type = "User",
                Ref = $"{baseUrl}/Users/{u.Id}",
            })],
            Meta = new ScimMeta { ResourceType = "Group", Location = $"{baseUrl}/Groups/{role.Id}" },
        };
    }

    private async Task<TRole?> FindRoleAsync(string id)
    {
        RequireCompany();
        if (!Guid.TryParse(id, CultureInfo.InvariantCulture, out var guid))
            return null;
        return await ManagedRoles().FirstOrDefaultAsync(r => r.Id == guid);
    }

    private IQueryable<TRole> ManagedRoles()
    {
        var company = RequireCompany();
        var query = roles.Roles.Where(r => r.TenantId == company);
        if (Options.ManagedRoles.Contains("*"))
            return query;

        var names = Options.ManagedRoles.Select(n => roles.NormalizeKey(n)).ToList();
        return query.Where(r => names.Contains(r.NormalizedName!));
    }

    private bool IsManaged(string name)
        => Options.ManagedRoles.Contains("*") || Options.ManagedRoles.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    private static void RequireSameName(TRole role, string? displayName)
    {
        if (displayName is not null && !string.Equals(displayName, role.Name, StringComparison.OrdinalIgnoreCase))
            throw new ScimException(400, "mutability", "A group cannot be renamed here: permissions are granted to the role by name.");
    }

    // ── Shared ─────────────────────────────────────────────────────

    private Guid RequireCompany()
        => tenant.TenantId is { } id && !tenant.IsHost
            ? id
            : throw new ScimException(403, null, "SCIM works inside one company: use an integration client bound to a company.");

    private (int Skip, int Take) Page(int startIndex, int count)
    {
        var skip = Math.Max(startIndex, 1) - 1;
        var take = Math.Clamp(count, 0, Math.Max(Options.MaxPageSize, 1));
        return (skip, take);
    }

    private List<ScimPatchOperation> Operations(ScimPatchRequest patch)
    {
        if (patch.Operations is not { Count: > 0 } operations)
            throw new ScimException(400, "invalidValue", "A PATCH needs at least one operation.");
        if (operations.Count > Options.MaxPatchOperations)
            throw new ScimException(400, "invalidValue", $"A PATCH may carry at most {Options.MaxPatchOperations} operations.");
        return operations;
    }

    private static string OperationName(ScimPatchOperation operation)
    {
        var op = operation.Op?.Trim().ToLowerInvariant();
        return op is "add" or "remove" or "replace"
            ? op
            : throw new ScimException(400, "invalidValue", "An operation is add, remove or replace.");
    }

    private static string RequireText(JsonElement? value, string name)
        => value is { ValueKind: JsonValueKind.String } v && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new ScimException(400, "invalidValue", $"{name} needs a text value.");

    private static string? StringProperty(JsonElement? value, string name)
        => value is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool RequireBool(JsonElement? value, string name) => value switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        // Some identity providers send the flag as text.
        { ValueKind: JsonValueKind.String } v when bool.TryParse(v.GetString(), out var flag) => flag,
        _ => throw new ScimException(400, "invalidValue", $"{name} needs true or false."),
    };

    private async Task AddMemberAsync(Guid userId, Guid company, CancellationToken ct)
    {
        if (services.GetService<ITenantMemberProvisioner>() is not { } provisioner)
            return;
        try
        {
            await provisioner.AddMemberAsync(userId, company, ct);
        }
        catch (ConflictException ex)
        {
            throw new ScimException(409, null, ex.Message);
        }
    }

    private async Task RemoveMemberAsync(Guid userId, Guid company, CancellationToken ct)
    {
        if (services.GetService<ITenantMemberProvisioner>() is { } provisioner)
            await provisioner.RemoveMemberAsync(userId, company, ct);
    }

    private static ScimException FromIdentity(IdentityResult result)
    {
        var duplicate = result.Errors.Any(e => e.Code.StartsWith("Duplicate", StringComparison.Ordinal));
        var detail = string.Join(" ", result.Errors.Select(e => e.Description));
        return duplicate ? new ScimException(409, "uniqueness", detail) : new ScimException(400, "invalidValue", detail);
    }

    private void Audit(string action, ModulusUser user)
        => services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = user.TenantId,
            Actor = services.GetService<ICurrentUser>()?.UserId?.ToString(),
            Target = $"user:{user.Id}",
        });

    private void Audit(string action, TRole role)
        => services.GetService<ISecurityAuditLog>()?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Identity,
            Action = action,
            Outcome = SecurityAuditOutcomes.Success,
            TenantId = role.TenantId,
            Actor = services.GetService<ICurrentUser>()?.UserId?.ToString(),
            Target = $"role:{role.Id}",
        });

    private ValueTask NotifyRoleChangedAsync(TRole role, string reason, Guid company)
        => services.GetServices<IAccessChangeObserver>().NotifyAccessChangedAsync(
            new AccessChange { Kind = AccessChangeKinds.Role, Reason = reason, TenantId = company }, logger);
}
