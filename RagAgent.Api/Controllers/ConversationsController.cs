using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RagAgent.Api.Dtos;
using RagAgent.Core;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/agent/conversations")]
public sealed class ConversationsController(
    IConversationStore conversationStore,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListConversationsAsync()
    {
        if (!(configuration.GetValue<bool?>("Conversations:ListEnabled") ?? true))
        {
            return NotFound();
        }

        try
        {
            var ids = await conversationStore.ListConversationIdsAsync();
            return Ok(ids.Select(id => new ConversationSummaryDto(id)));
        }
        catch (ConversationStoreValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ConversationStoreNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("{conversationId}")]
    public async Task<IActionResult> GetConversationAsync(string conversationId)
    {
        try
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
        catch (ConversationStoreValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ConversationStoreNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> DeleteConversationAsync(string conversationId)
    {
        try
        {
            await conversationStore.DeleteAsync(conversationId);
            return NoContent();
        }
        catch (ConversationStoreValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ConversationStoreNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
