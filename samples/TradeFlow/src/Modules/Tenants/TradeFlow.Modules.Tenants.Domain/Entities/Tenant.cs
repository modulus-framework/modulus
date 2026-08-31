using System.Text.Json;
using TradeFlow.Modules.Tenants.Domain.Constants;
using TradeFlow.Modules.Tenants.Domain.Events;
using TradeFlow.Modules.Tenants.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace TradeFlow.Modules.Tenants.Domain.Entities;

public sealed class Tenant : AggregateRoot, IAuditableEntity
{
    public new TenantId Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Subdomain Subdomain { get; private set; } = null!;
    public string DatabaseConnectionString { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public JsonDocument Features { get; private set; } = null!;
    public JsonDocument Settings { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? LastModifiedAtUtc { get; private set; }
    public string? LastModifiedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public string? DeletedBy { get; private set; }

    private Tenant() { }

    private Tenant(
        TenantId id,
        string name,
        Subdomain subdomain,
        string databaseConnectionString,
        JsonDocument features,
        JsonDocument settings,
        string? createdBy)
    {
        base.Id = id.Value;
        Id = id;
        Name = name;
        Subdomain = subdomain;
        DatabaseConnectionString = databaseConnectionString;
        Features = features;
        Settings = settings;
        IsActive = true;
        IsDeleted = false;
        CreatedAtUtc = DateTime.UtcNow;
        CreatedBy = createdBy;
        LastModifiedAtUtc = DateTime.UtcNow;
        LastModifiedBy = createdBy;

        Raise(new TenantCreatedDomainEvent(
            Guid.NewGuid(),
            id.Value,
            name,
            subdomain.Value,
            CreatedAtUtc));
    }

    public static Result<Tenant> Create(
        TenantId id,
        string name,
        Subdomain subdomain,
        string databaseConnectionString,
        JsonDocument features,
        JsonDocument settings,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(TenantErrors.EmptyName);
        }

        if (name.Length > 200)
        {
            return Result.Failure<Tenant>(TenantErrors.NameTooLong);
        }

        if (string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            return Result.Failure<Tenant>(Error.Validation("Tenant.EmptyConnectionString", "Connection string cannot be empty"));
        }

        if (databaseConnectionString.Length > 2000)
        {
            return Result.Failure<Tenant>(TenantErrors.ConnectionStringTooLong);
        }

        return Result.Success(new Tenant(
            id,
            name.Trim(),
            subdomain,
            databaseConnectionString,
            features,
            settings,
            createdBy));
    }

    public Result Update(
        string name,
        string databaseConnectionString,
        string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(TenantErrors.EmptyName);
        }

        if (name.Length > 200)
        {
            return Result.Failure(TenantErrors.NameTooLong);
        }

        if (string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            return Result.Failure(Error.Validation("Tenant.EmptyConnectionString", "Connection string cannot be empty"));
        }

        if (databaseConnectionString.Length > 2000)
        {
            return Result.Failure(TenantErrors.ConnectionStringTooLong);
        }

        Name = name.Trim();
        DatabaseConnectionString = databaseConnectionString;
        LastModifiedAtUtc = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new TenantUpdatedDomainEvent(
            Guid.NewGuid(),
            Id.Value,
            Name,
            modifiedBy ?? string.Empty,
            LastModifiedAtUtc.Value));

        return Result.Success();
    }

    public Result Activate(string modifiedBy)
    {
        if (IsActive)
        {
            return Result.Failure(TenantErrors.AlreadyActive);
        }

        IsActive = true;
        LastModifiedAtUtc = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new TenantActivatedDomainEvent(
            Guid.NewGuid(),
            Id.Value,
            Name,
            LastModifiedAtUtc.Value));

        return Result.Success();
    }

    public Result Deactivate(string modifiedBy)
    {
        if (!IsActive)
        {
            return Result.Failure(TenantErrors.AlreadyInactive);
        }

        IsActive = false;
        LastModifiedAtUtc = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new TenantDeactivatedDomainEvent(
            Guid.NewGuid(),
            Id.Value,
            Name,
            LastModifiedAtUtc.Value));

        return Result.Success();
    }

    public Result Delete(string deletedBy)
    {
        if (IsActive)
        {
            return Result.Failure(TenantErrors.CannotDeleteActiveTenant);
        }

        IsDeleted = true;
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = deletedBy;
        IncrementVersion();

        Raise(new TenantDeletedDomainEvent(
            Guid.NewGuid(),
            Id.Value,
            Name,
            deletedBy ?? string.Empty,
            DeletedAtUtc.Value));

        return Result.Success();
    }

    public Result UpdateFeatures(JsonDocument newFeatures, string modifiedBy)
    {
        Features = newFeatures;
        LastModifiedAtUtc = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new TenantFeaturesUpdatedDomainEvent(
            Guid.NewGuid(),
            Id.Value,
            Name,
            modifiedBy ?? string.Empty,
            LastModifiedAtUtc.Value));

        return Result.Success();
    }

    public Result UpdateSettings(JsonDocument newSettings, string modifiedBy)
    {
        Settings = newSettings;
        LastModifiedAtUtc = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new TenantSettingsUpdatedDomainEvent(
            Guid.NewGuid(),
            Id.Value,
            Name,
            modifiedBy ?? string.Empty,
            LastModifiedAtUtc.Value));

        return Result.Success();
    }

    public void SetCreatedBy(string createdBy) => CreatedBy = createdBy;
    public void SetLastModifiedBy(string modifiedBy) => LastModifiedBy = modifiedBy;

    DateTime IAuditableEntity.CreatedAt { get => CreatedAtUtc; set => CreatedAtUtc = value; }
    string? IAuditableEntity.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditableEntity.UpdatedAt { get => LastModifiedAtUtc; set => LastModifiedAtUtc = value; }
    string? IAuditableEntity.UpdatedBy { get => LastModifiedBy; set => LastModifiedBy = value; }
}
