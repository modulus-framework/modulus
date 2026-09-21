// Shared kernel marker — cross-module primitives live here as the app grows
// (e.g. shared value objects, Result types). Kept minimal for the MVP sample.
namespace Meetup.Shared.Domain;

/// <summary>Marker type used to reference the Shared.Domain assembly.</summary>
public static class AssemblyReference
{
    public static System.Reflection.Assembly Assembly => typeof(AssemblyReference).Assembly;
}
