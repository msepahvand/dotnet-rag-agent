using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace RagAgent.AgentCore;

public sealed class DynamoDbConversationLockClient(
    IAmazonDynamoDB dynamoDb,
    AgentCoreMemoryOptions options) : IDynamoDbConversationLockClient
{
    public async Task<bool> TryAcquireAsync(
        string conversationKey,
        string ownerToken,
        long nowUnixSeconds,
        long leaseExpiresUnixSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = options.LockTableName,
                    Key = CreateKey(conversationKey),
                    UpdateExpression = "SET OwnerToken = :owner, LeaseExpiresAt = :expires",
                    ConditionExpression = "attribute_not_exists(LockKey) OR LeaseExpiresAt <= :now",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":owner"] = new() { S = ownerToken },
                        [":expires"] = new() { N = leaseExpiresUnixSeconds.ToString() },
                        [":now"] = new() { N = nowUnixSeconds.ToString() }
                    }
                },
                cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<bool> RenewAsync(
        string conversationKey,
        string ownerToken,
        long nowUnixSeconds,
        long leaseExpiresUnixSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = options.LockTableName,
                    Key = CreateKey(conversationKey),
                    UpdateExpression = "SET LeaseExpiresAt = :expires",
                    ConditionExpression = "OwnerToken = :owner AND LeaseExpiresAt > :now",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":owner"] = new() { S = ownerToken },
                        [":expires"] = new() { N = leaseExpiresUnixSeconds.ToString() },
                        [":now"] = new() { N = nowUnixSeconds.ToString() }
                    }
                },
                cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task ReleaseAsync(
        string conversationKey,
        string ownerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.DeleteItemAsync(
                new DeleteItemRequest
                {
                    TableName = options.LockTableName,
                    Key = CreateKey(conversationKey),
                    ConditionExpression = "OwnerToken = :owner",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":owner"] = new() { S = ownerToken }
                    }
                },
                cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            // Another owner has already acquired the expired lease.
        }
    }

    private static Dictionary<string, AttributeValue> CreateKey(string conversationKey) =>
        new() { ["LockKey"] = new AttributeValue { S = conversationKey } };
}
