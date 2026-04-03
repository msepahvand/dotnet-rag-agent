using Microsoft.AspNetCore.Mvc;
using VectorSearch.Api.Dtos;
using VectorSearch.Core;

namespace VectorSearch.Api.Controllers;

[ApiController]
[Route("api/agent/conversations")]
public sealed class ConversationsController(IConversationStore conversationStore) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListConversations()
    {
        var ids = await conversationStore.ListConversationIdsAsync();
        return Ok(ids.Select(id => new ConversationSummaryDto(id)));
    }

    [HttpGet("{conversationId}")]
    public async Task<IActionResult> GetConversation(string conversationId)
    {
        var history = await conversationStore.GetHistoryAsync(conversationId);

        if (history.Count == 0)
        {
            return NotFound();
        }

        return Ok(new ConversationHistoryDto
        {
            ConversationId = conversationId,
            Messages = history.Select(m => new ConversationMessageDto(m.Role, m.Content)).ToList()
        });
    }

    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> DeleteConversation(string conversationId)
    {
        await conversationStore.DeleteAsync(conversationId);
        return NoContent();
    }
}
