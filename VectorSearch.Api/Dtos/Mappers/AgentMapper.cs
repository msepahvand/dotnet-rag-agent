using VectorSearch.Core.Models;

namespace VectorSearch.Api.Dtos.Mappers;

public static class AgentMapper
{
    public static AgentAskRequest ToModel(AskRequestDto dto) =>
        new() { Question = dto.Question, TopK = dto.TopK, ConversationId = dto.ConversationId };

    public static AskResponseDto ToDto(AgentAskResponse model) =>
        new()
        {
            ConversationId = model.ConversationId,
            ToolsUsed = model.ToolsUsed,
            Grounded = model.Grounded,
            Answer = model.Answer,
            Citations = model.Citations.Select(CitationMapper.ToDto).ToList(),
            Sources = model.Sources.Select(SourceMapper.ToDto).ToList()
        };
}

public static class CitationMapper
{
    public static CitationDto ToDto(Citation model) =>
        new() { PostId = model.PostId, Quote = model.Quote };
}

public static class SourceMapper
{
    public static SourceDto ToDto(AgentSource model) =>
        new()
        {
            PostId = model.PostId,
            Title = model.Title,
            Snippet = model.Snippet,
            Distance = model.Distance
        };
}
