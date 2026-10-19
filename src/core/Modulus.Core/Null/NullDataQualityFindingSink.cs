namespace Modulus.Core.Null;

using Modulus.Core.Abstractions.DataQuality;

/// <summary>The default sink: findings are discarded. Register a real sink to keep them.</summary>
public sealed class NullDataQualityFindingSink : IDataQualityFindingSink
{
    public void Record(DataQualityFinding finding)
    {
    }
}
