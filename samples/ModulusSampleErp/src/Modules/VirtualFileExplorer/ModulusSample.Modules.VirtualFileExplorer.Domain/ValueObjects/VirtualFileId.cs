namespace ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;

public readonly record struct VirtualFileId(Guid Value)
{
    public static VirtualFileId Create() => new(Guid.NewGuid());
    public static VirtualFileId From(Guid value) => new(value);
}
