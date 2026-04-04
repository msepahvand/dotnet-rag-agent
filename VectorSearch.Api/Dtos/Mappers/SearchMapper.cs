using VectorSearch.Core.Models;

namespace VectorSearch.Api.Dtos.Mappers;

public static class SearchMapper
{
    public static SearchResultDto ToDto(SearchResult model) =>
        new()
        {
            PostId = model.PostId,
            Title = model.Title,
            Distance = model.Distance,
            UserId = model.UserId
        };
}
