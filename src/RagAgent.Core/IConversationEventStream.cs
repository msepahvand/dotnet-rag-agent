using RagAgent.Core.Models;

namespace RagAgent.Core;

public interface IConversationEventStream
{
    IAsyncEnumerable<ConversationEvent> Subscribe(CancellationToken cancellationToken = default);
}
