namespace Modulus.Messaging;

/// <summary>
/// Marks the property whose value orders a message within its partition (FR-14). Messages with the same key are
/// handled in send order by an <see cref="ConsumerOptions.Ordered"/> consumer. The value is converted with
/// <see cref="object.ToString"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PartitionKeyAttribute : Attribute
{
}
