namespace RagAgent.Core;

public sealed class ConversationStoreValidationException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class ConversationStoreNotFoundException(string message, Exception innerException)
    : Exception(message, innerException);
