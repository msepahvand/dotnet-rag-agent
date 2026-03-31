using VectorSearch.Core.Models;

namespace VectorSearch.Api.Dtos.Mappers;

public static class PostMapper
{
    public static PostDto ToDto(Post model) =>
        new()
        {
            Id = model.Id,
            UserId = model.UserId,
            Title = model.Title,
            Body = model.Body
        };
}
