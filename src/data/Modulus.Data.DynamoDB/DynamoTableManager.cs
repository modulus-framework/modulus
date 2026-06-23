namespace Modulus.Data.DynamoDB;

using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

public sealed class DynamoTableManager(
    IAmazonDynamoDB              dynamo,
    IOptions<DynamoOptions>      opts)
{
    public async Task EnsureTableAsync(
        string tableName,
        string hashKey,
        string? rangeKey = null,
        BillingMode? billingMode = null,
        CancellationToken ct = default)
    {
        var fullName = opts.Value.TablePrefix + tableName;
        var actualBillingMode = billingMode ?? BillingMode.PAY_PER_REQUEST;

        try
        {
            await dynamo.DescribeTableAsync(fullName, ct);
            return; // table exists
        }
        catch (ResourceNotFoundException) { }

        var keySchema = new List<KeySchemaElement>
        {
            new(hashKey, KeyType.HASH),
        };
        var attrDefs = new List<AttributeDefinition>
        {
            new(hashKey, ScalarAttributeType.S),
        };

        if (rangeKey is not null)
        {
            keySchema.Add(new(rangeKey, KeyType.RANGE));
            attrDefs.Add(new(rangeKey, ScalarAttributeType.S));
        }

        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName            = fullName,
            KeySchema            = keySchema,
            AttributeDefinitions = attrDefs,
            BillingMode          = actualBillingMode,
        }, ct);
    }
}