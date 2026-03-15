using VectorSearch.Core;

namespace VectorSearch.Api.Contracts.Responses;

public sealed record IndexSinglePostResponse(string Message, Post Post);
