using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel.ChatCompletion;
using VectorSearch.Core;
using VectorSearch.S3;

namespace VectorSearch.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers();
        
        // Add Vector Search services
        builder.Services.AddVectorSearch(builder.Configuration);
        
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Vector Search API",
                Version = "v1",
                Description = "Semantic search API powered by vector embeddings."
            });
        });

        var vectorProvider = builder.Configuration["VectorStore:Provider"] ?? "S3Vectors";
        var initializeOnStartupOverride = builder.Configuration.GetValue<bool?>("VectorStore:InitializeOnStartup");
        var requiresStartupInitialization =
            vectorProvider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase) ||
            vectorProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase);
        var shouldInitializeOnStartup = initializeOnStartupOverride ?? requiresStartupInitialization;

        var app = builder.Build();

        // Ensure vector store is initialized only when required (skip in test environment)
        if (!app.Environment.IsEnvironment("Testing") && shouldInitializeOnStartup)
        {
            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var vectorService = scope.ServiceProvider.GetRequiredService<IVectorService>();
                try
                {
                    await vectorService.EnsureInitializedAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Vector store initialization failed at startup. Service will continue running.");
                }
            }
        }

        // Configure the HTTP request pipeline.
            var swaggerEnabled = app.Configuration.GetValue<bool?>("Swagger:Enabled") ?? app.Environment.IsDevelopment();
            if (swaggerEnabled)
            {
                app.UseSwagger();
                app.UseSwaggerUI(options =>
                {
                    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Vector Search API v1");
                    options.RoutePrefix = "swagger";
                });
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            // Minimal API endpoints for vector search
            app.MapPost("/api/index/all", async (IPostService postService, IEmbeddingService embeddingService, IVectorService vectorService) =>
            {
                var posts = await postService.GetAllPostsAsync();
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(posts);
                
                var postsWithEmbeddings = posts
                    .Join(embeddings, 
                        p => p.Id, 
                        e => e.PostId, 
                        (p, e) => (Post: p, Embedding: e.Embedding))
                    .ToList();

                await vectorService.IndexPostsBatchAsync(postsWithEmbeddings);
                
                return Results.Ok(new { Message = $"Indexed {posts.Count} posts successfully", Count = posts.Count });
            })
            .WithName("IndexAllPosts")
            .WithOpenApi();

            app.MapPost("/api/index/{id:int}", async (int id, IPostService postService, IEmbeddingService embeddingService, IVectorService vectorService) =>
            {
                var post = await postService.GetPostByIdAsync(id);
                if (post == null)
                    return Results.NotFound(new { Message = $"Post {id} not found" });

                var embedding = await embeddingService.GenerateEmbeddingAsync($"{post.Title}\n\n{post.Body}");
                await vectorService.IndexPostAsync(post, embedding);
                
                return Results.Ok(new { Message = $"Indexed post {id} successfully", Post = post });
            })
            .WithName("IndexSinglePost")
            .WithOpenApi();

            app.MapGet("/api/search", async (string query, int topK, IVectorService vectorService) =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return Results.BadRequest(new { Message = "Query cannot be empty" });

                var results = await vectorService.SemanticSearchAsync(query, topK <= 0 ? 10 : topK);
                return Results.Ok(results);
            })
            .WithName("SemanticSearch")
            .WithOpenApi();

            app.MapPost("/api/agent/ask", async (AgentAskRequest request, IVectorService vectorService, IPostService postService, IServiceProvider serviceProvider, ILogger<Program> logger) =>
            {
                if (string.IsNullOrWhiteSpace(request.Question))
                    return Results.BadRequest(new { Message = "Question cannot be empty" });

                var topK = request.TopK <= 0 ? 5 : Math.Min(request.TopK, 10);
                var searchResults = await vectorService.SemanticSearchAsync(request.Question, topK);

                if (searchResults.Count == 0)
                {
                    return Results.Ok(new AgentAskResponse
                    {
                        ToolUsed = "semantic-search",
                        Grounded = false,
                        Answer = "I couldn't find supporting sources in the indexed content. Try indexing more data or rephrasing the question.",
                        Sources = []
                    });
                }

                var sourceCandidates = await Task.WhenAll(
                    searchResults.Select(async result =>
                    {
                        var post = await postService.GetPostByIdAsync(result.PostId);
                        var snippet = post == null
                            ? string.Empty
                            : TrimForSnippet(post.Body, 220);

                        return new AgentSource
                        {
                            PostId = result.PostId,
                            Title = result.Title,
                            Distance = result.Distance,
                            Snippet = snippet
                        };
                    }));

                var sources = sourceCandidates
                    .Where(source => !string.IsNullOrWhiteSpace(source.Title))
                    .ToList();

                var answer = await BuildGroundedAnswerAsync(request.Question, sources, serviceProvider, logger);

                return Results.Ok(new AgentAskResponse
                {
                    ToolUsed = "semantic-search",
                    Grounded = sources.Count > 0,
                    Answer = answer,
                    Sources = sources
                });
            })
            .WithName("AgentAsk")
            .WithOpenApi();

            app.MapGet("/api/posts", async (IPostService postService) =>
            {
                var posts = await postService.GetAllPostsAsync();
                return Results.Ok(posts);
            })
            .WithName("GetAllPosts")
            .WithOpenApi();

            app.MapGet("/api/posts/{id:int}", async (int id, IPostService postService) =>
            {
                var post = await postService.GetPostByIdAsync(id);
                return post != null ? Results.Ok(post) : Results.NotFound();
            })
        .WithName("GetPost")
        .WithOpenApi();

        app.Run();
    }

    private static async Task<string> BuildGroundedAnswerAsync(string question, List<AgentSource> sources, IServiceProvider serviceProvider, ILogger logger)
    {
        if (sources.Count == 0)
        {
            return "I couldn't produce a grounded answer because there were no supporting sources.";
        }

        var deterministicAnswer = BuildDeterministicGroundedAnswer(question, sources);

        try
        {
            var chatService = serviceProvider.GetService(typeof(IChatCompletionService)) as IChatCompletionService;
            if (chatService == null)
            {
                return deterministicAnswer;
            }

            var sourceContext = string.Join(
                "\n",
                sources.Select(source =>
                    $"[PostId: {source.PostId}] Title: {source.Title}\nSnippet: {source.Snippet}"));

            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage("You are a grounded assistant. Answer only from the provided sources. If the sources are insufficient, say you do not have enough information. Include citations using [PostId: N] for each key claim.");
            chatHistory.AddUserMessage($"Question:\n{question}\n\nSources:\n{sourceContext}\n\nProvide a concise answer with citations.");

            var response = await chatService.GetChatMessageContentsAsync(chatHistory);
            var llmAnswer = response.FirstOrDefault()?.Content?.Trim();

            return string.IsNullOrWhiteSpace(llmAnswer)
                ? deterministicAnswer
                : llmAnswer;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to deterministic grounded answer because Bedrock chat generation failed.");
            return deterministicAnswer;
        }
    }

    private static string BuildDeterministicGroundedAnswer(string question, List<AgentSource> sources)
    {
        var topSources = sources.Take(3).ToList();
        var evidence = string.Join(
            "\n",
            topSources.Select((source, index) =>
                $"{index + 1}. {source.Title} [PostId: {source.PostId}] - {source.Snippet}"));

        return $"Grounded answer for: {question}\n\nSupporting evidence:\n{evidence}";
    }

    private static string TrimForSnippet(string content, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        if (content.Length <= maxLength)
        {
            return content;
        }

        return $"{content[..maxLength].TrimEnd()}...";
    }
}

public record AgentAskRequest
{
    public string Question { get; init; } = string.Empty;
    public int TopK { get; init; } = 5;
}

public record AgentSource
{
    public int PostId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public double Distance { get; init; }
}

public record AgentAskResponse
{
    public string ToolUsed { get; init; } = "semantic-search";
    public bool Grounded { get; init; }
    public string Answer { get; init; } = string.Empty;
    public List<AgentSource> Sources { get; init; } = [];
}
