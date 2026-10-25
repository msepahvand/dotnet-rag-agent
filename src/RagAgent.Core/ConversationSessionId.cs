using System.Security.Cryptography;
using System.Text;

namespace RagAgent.Core;

public static class ConversationSessionId
{
    public static string Create(string conversationId)
    {
        ArgumentNullException.ThrowIfNull(conversationId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(conversationId))).ToLowerInvariant();
    }
}
