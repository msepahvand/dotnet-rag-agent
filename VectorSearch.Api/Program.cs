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
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        // Ensure vector store is initialized (skip in test environment)
        if (!app.Environment.IsEnvironment("Testing"))
        {
            using (var scope = app.Services.CreateScope())
            {
                var vectorService = scope.ServiceProvider.GetRequiredService<IVectorService>();
                await vectorService.EnsureInitializedAsync();
            }
        }

        // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
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
}
