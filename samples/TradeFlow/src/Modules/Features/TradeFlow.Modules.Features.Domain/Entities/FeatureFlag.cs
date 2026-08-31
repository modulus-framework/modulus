using ModulusSample.Modules.Features.Domain.Events;
using ModulusSample.Modules.Features.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace ModulusSample.Modules.Features.Domain.Entities;

public sealed class FeatureFlag : AggregateRoot, IAuditableEntity, IHasTenantId
{
    public new FeatureFlagId Id { get; private set; } = null!;
    public FeatureKey Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsEnabled { get; private set; }
    public Guid TenantId { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public DateTime LastModifiedAt => UpdatedAt ?? CreatedAt;
    public string? LastModifiedBy => UpdatedBy;


    private FeatureFlag() { }

    private FeatureFlag(
        FeatureFlagId id,
        FeatureKey key,
        string name,
        string? description,
        bool isEnabled,
        Guid tenantId,
        string? createdBy)
    {
        base.Id = id.Value;
        Id = id;
        Key = key;
        Name = name;
        Description = description;
        IsEnabled = isEnabled;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = createdBy;

        Raise(new FeatureFlagCreatedDomainEvent(id, key.Value, name, tenantId, DateTime.UtcNow));
    }

    public static Result<FeatureFlag> Create(
        FeatureFlagId id,
        FeatureKey key,
        string name,
        string? description,
        bool isEnabled,
        Guid tenantId,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<FeatureFlag>(Error.Validation("FeatureFlag.EmptyName", "Name cannot be empty"));
        }

        if (name.Length > 200)
        {
            return Result.Failure<FeatureFlag>(Error.Validation("FeatureFlag.NameTooLong", "Name cannot exceed 200 characters"));
        }

        if (description != null && description.Length > 500)
        {
            return Result.Failure<FeatureFlag>(Error.Validation("FeatureFlag.DescriptionTooLong", "Description cannot exceed 500 characters"));
        }

        return Result.Success(new FeatureFlag(id, key, name, description, isEnabled, tenantId, createdBy));
    }

    public Result Update(string name, string? description, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("FeatureFlag.EmptyName", "Name cannot be empty"));
        }

        if (name.Length > 200)
        {
            return Result.Failure(Error.Validation("FeatureFlag.NameTooLong", "Name cannot exceed 200 characters"));
        }

        if (description != null && description.Length > 500)
        {
            return Result.Failure(Error.Validation("FeatureFlag.DescriptionTooLong", "Description cannot exceed 500 characters"));
        }

        Name = name;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = modifiedBy;
        IncrementVersion();

        Raise(new FeatureFlagUpdatedDomainEvent(Id, Key.Value, Name, TenantId, DateTime.UtcNow));

        return Result.Success();
    }

    public Result Toggle(bool isEnabled, string modifiedBy)
    {
        if (IsEnabled == isEnabled)
        {
            return Result.Success();
        }

        IsEnabled = isEnabled;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = modifiedBy;
        IncrementVersion();

        Raise(new FeatureFlagToggledDomainEvent(Id, Key.Value, IsEnabled, TenantId, DateTime.UtcNow));

        return Result.Success();
    }

    public Result Delete(string deletedBy)
    {
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = deletedBy;
        IncrementVersion();

        Raise(new FeatureFlagDeletedDomainEvent(Id, Key.Value, TenantId, DateTime.UtcNow));

        return Result.Success();
    }

}
