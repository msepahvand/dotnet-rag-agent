using RagAgent.Api.Dtos;

namespace RagAgent.Api.Contracts.Responses;

public sealed record IndexSinglePostResponse(string Message, PostDto Post);
