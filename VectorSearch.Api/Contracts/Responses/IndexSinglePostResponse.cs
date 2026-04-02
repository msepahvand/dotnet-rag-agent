using VectorSearch.Api.Dtos;

namespace VectorSearch.Api.Contracts.Responses;

public sealed record IndexSinglePostResponse(string Message, PostDto Post);
