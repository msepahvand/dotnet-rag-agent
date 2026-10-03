namespace RagAgent.AgentCore;

public interface IDistributedConversationLock
{
    Task<IDistributedConversationLockLease> AcquireAsync(
        string conversationKey,
        CancellationToken cancellationToken = default);
}

public interface IDistributedConversationLockLease : IAsyncDisposable
{
    CancellationToken CancellationToken { get; }

    void EnsureValid();
}

public interface IDynamoDbConversationLockClient
{
    Task<bool> TryAcquireAsync(
        string conversationKey,
        string ownerToken,
        long nowUnixSeconds,
        long leaseExpiresUnixSeconds,
        CancellationToken cancellationToken);

    Task<bool> RenewAsync(
        string conversationKey,
        string ownerToken,
        long nowUnixSeconds,
        long leaseExpiresUnixSeconds,
        CancellationToken cancellationToken);

    Task ReleaseAsync(string conversationKey, string ownerToken, CancellationToken cancellationToken);
}
