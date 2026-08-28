using System.Reflection;

namespace ProcureFlow.Modules.Customs.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}