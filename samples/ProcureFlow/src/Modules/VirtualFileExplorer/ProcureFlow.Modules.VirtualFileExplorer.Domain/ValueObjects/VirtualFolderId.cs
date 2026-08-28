namespace ProcureFlow.Modules.VirtualFileExplorer.Domain.ValueObjects;

public readonly record struct VirtualFolderId(Guid Value)
{
    public static VirtualFolderId Create() => new(Guid.NewGuid());
    public static VirtualFolderId From(Guid value) => new(value);
}
