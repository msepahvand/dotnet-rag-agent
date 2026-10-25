using Microsoft.AspNetCore.Mvc;
using RagAgent.Api.Dtos;
using RagAgent.Api.Dtos.Mappers;
using RagAgent.Core;
using RagAgent.Core.Models;

namespace RagAgent.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class EvaluationController(IEvaluationAgent evaluationAgent) : ControllerBase
{
    /// <summary>
    /// Default questions used when none are supplied in the request body.
    /// These cover common Hacker News discussion themes and exercise the full pipeline
    /// without requiring labelled expected post IDs.
    /// </summary>
    private static readonly IReadOnlyList<EvaluationQuestion> DefaultQuestions =
    [
        new("What are the tradeoffs between microservices and monolithic architectures?", []),
        new("How do developers approach technical debt in large codebases?", []),
        new("What are common pitfalls when adopting Kubernetes in production?", []),
        new("How should teams think about API versioning strategies?", []),
        new("What are best practices for database schema migrations?", []),
    ];

    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluationReportDto>> EvaluateAsync(
        [FromBody] EvaluationRequestDto? request)
    {
        var questions = request?.Questions is { Count: > 0 } supplied
            ? supplied.Select(q => new EvaluationQuestion(q.Question, q.ExpectedPostIds)).ToList()
            : DefaultQuestions;

        var topK = request?.TopK ?? 5;
        var report = await evaluationAgent.EvaluateAsync(questions, topK);
        return Ok(EvaluationMapper.ToDto(report));
    }
}
