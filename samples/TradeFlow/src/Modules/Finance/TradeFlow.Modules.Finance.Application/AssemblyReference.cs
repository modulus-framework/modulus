using System.Reflection;

namespace TradeFlow.Modules.Finance.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}