using ModulusSample.Modules.Settings.Domain.Events;
using ModulusSample.Modules.Settings.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using Modulus.Core.Abstractions.Entities;

namespace ModulusSample.Modules.Settings.Domain.Entities;

public sealed class Setting : AggregateRoot, IAuditableEntity
{
    public new SettingId Id { get; private set; }
    public SettingKey Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public bool IsPublic { get; private set; }
    public Guid TenantId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime LastModifiedAt { get; private set; }
    public string? LastModifiedBy { get; private set; }

    private Setting() { }

    private Setting(
        SettingId id,
        SettingKey key,
        string value,
        string category,
        string description,
        bool isPublic,
        Guid tenantId,
        string? createdBy)
    {
        base.Id = id.Value;
        Id = id;
        Key = key;
        Value = value;
        Category = category;
        Description = description;
        IsPublic = isPublic;
        TenantId = tenantId;
        CreatedAt = DateTime.UtcNow;
        CreatedBy = createdBy;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = createdBy;

        Raise(new SettingCreatedDomainEvent(id, key.Value, category, tenantId, DateTime.UtcNow));
    }

    public static Result<Setting> Create(
        SettingId id,
        SettingKey key,
        string value,
        string category,
        string description,
        bool isPublic,
        Guid tenantId,
        string? createdBy)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure<Setting>(Error.Validation("Setting.EmptyCategory", "Category cannot be empty"));
        }

        if (category.Length > 100)
        {
            return Result.Failure<Setting>(Error.Validation("Setting.TooLongCategory", "Category cannot exceed 100 characters"));
        }

        if (description != null && description.Length > 500)
        {
            return Result.Failure<Setting>(Error.Validation("Setting.TooLongDescription", "Description cannot exceed 500 characters"));
        }

        return Result.Success(new Setting(id, key, value, category, description ?? string.Empty, isPublic, tenantId, createdBy));
    }

    public Result UpdateValue(string newValue, string modifiedBy)
    {
        if (Value == newValue)
        {
            return Result.Success();
        }

        string oldValue = Value;
        Value = newValue;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new SettingUpdatedDomainEvent(Id, Key.Value, oldValue, newValue, modifiedBy ?? string.Empty, DateTime.UtcNow));

        return Result.Success();
    }

    public Result UpdateMetadata(string category, string description, bool isPublic, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure(Error.Validation("Setting.EmptyCategory", "Category cannot be empty"));
        }

        if (category.Length > 100)
        {
            return Result.Failure(Error.Validation("Setting.TooLongCategory", "Category cannot exceed 100 characters"));
        }

        if (description != null && description.Length > 500)
        {
            return Result.Failure(Error.Validation("Setting.TooLongDescription", "Description cannot exceed 500 characters"));
        }

        Category = category;
        Description = description ?? string.Empty;
        IsPublic = isPublic;
        LastModifiedAt = DateTime.UtcNow;
        LastModifiedBy = modifiedBy;
        IncrementVersion();

        Raise(new SettingUpdatedDomainEvent(Id, Key.Value, Value, Value, modifiedBy ?? string.Empty, DateTime.UtcNow));

        return Result.Success();
    }

    public void Delete(string deletedBy)
    {
        Raise(new SettingDeletedDomainEvent(Id, Key.Value, Value, deletedBy ?? string.Empty, DateTime.UtcNow));
    }

    public void SetCreatedBy(string createdBy) => CreatedBy = createdBy;
    public void SetLastModifiedBy(string modifiedBy) => LastModifiedBy = modifiedBy;

    DateTime IAuditableEntity.CreatedAt { get => CreatedAt; set => CreatedAt = value; }
    string? IAuditableEntity.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditableEntity.UpdatedAt { get => LastModifiedAt; set { if (value.HasValue) LastModifiedAt = value.Value; } }
    string? IAuditableEntity.UpdatedBy { get => LastModifiedBy; set => LastModifiedBy = value; }
}