using System.Reflection;

namespace TradeFlow.Modules.Costing.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}