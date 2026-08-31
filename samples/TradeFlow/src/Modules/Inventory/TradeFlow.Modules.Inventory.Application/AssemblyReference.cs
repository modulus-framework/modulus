using System.Reflection;

namespace TradeFlow.Modules.Inventory.Application;

public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}