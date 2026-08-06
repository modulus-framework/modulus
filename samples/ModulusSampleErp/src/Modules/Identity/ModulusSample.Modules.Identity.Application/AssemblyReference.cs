using System.Reflection;

namespace ModulusSample.Modules.Identity.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
