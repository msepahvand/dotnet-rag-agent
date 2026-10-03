using Microsoft.Extensions.Logging;

namespace RagAgent.AgentCore;

public sealed class DynamoDbConversationLockProvider(
    IDynamoDbConversationLockClient lockClient,
    ILogger<DynamoDbConversationLockProvider> logger) : IDistributedConversationLock
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public async Task<IDistributedConversationLockLease> AcquireAsync(
        string conversationKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);
        var ownerToken = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + AcquisitionTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (await lockClient.TryAcquireAsync(
                    conversationKey,
                    ownerToken,
                    now,
                    now + (long)LeaseDuration.TotalSeconds,
                    cancellationToken))
            {
                return new Lease(lockClient, logger, conversationKey, ownerToken, LeaseDuration);
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Could not acquire the conversation lock before the timeout.");
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }
    }

    private sealed class Lease : IDistributedConversationLockLease
    {
        private readonly CancellationTokenSource stopRenewal = new();
        private readonly CancellationTokenSource leaseLost = new();
        private readonly IDynamoDbConversationLockClient lockClient;
        private readonly string conversationKey;
        private readonly string ownerToken;
        private readonly Task renewalTask;
        private Exception? renewalFailure;

        public Lease(
            IDynamoDbConversationLockClient lockClient,
            ILogger logger,
            string conversationKey,
            string ownerToken,
            TimeSpan leaseDuration)
        {
            this.lockClient = lockClient;
            this.conversationKey = conversationKey;
            this.ownerToken = ownerToken;
            renewalTask = RenewLeaseAsync(
                lockClient,
                logger,
                conversationKey,
                ownerToken,
                leaseDuration,
                stopRenewal.Token);
        }

        public CancellationToken CancellationToken => leaseLost.Token;

        public void EnsureValid()
        {
            if (renewalFailure is not null)
            {
                throw new InvalidOperationException(
                    "The conversation lock lease could not be renewed; the operation may have overlapped another writer.",
                    renewalFailure);
            }
        }

        public async ValueTask DisposeAsync()
        {
            stopRenewal.Cancel();
            try
            {
                await renewalTask;
            }
            finally
            {
                try
                {
                    await lockClient.ReleaseAsync(conversationKey, ownerToken, CancellationToken.None);
                }
                finally
                {
                    stopRenewal.Dispose();
                    leaseLost.Dispose();
                }
            }

            EnsureValid();
        }

        private async Task RenewLeaseAsync(
            IDynamoDbConversationLockClient client,
            ILogger log,
            string key,
            string owner,
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromTicks(duration.Ticks / 3));
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (!await client.RenewAsync(
                            key,
                            owner,
                            now,
                            now + (long)duration.TotalSeconds,
                            cancellationToken))
                    {
                        renewalFailure = new InvalidOperationException("The conversation lock lease is no longer owned.");
                        leaseLost.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                renewalFailure = exception;
                leaseLost.Cancel();
                log.LogError(exception, "Failed to renew the distributed conversation lock.");
            }
        }
    }
}
