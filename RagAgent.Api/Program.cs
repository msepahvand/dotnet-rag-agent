using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.OpenApi.Models;
using RagAgent.Api.Extensions;
using RagAgent.Api.Services;
using RagAgent.Core;
using RagAgent.Agents;

namespace RagAgent.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddMemoryCache();
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        builder.Services.AddVectorSearch(builder.Configuration);
        builder.Services.AddVectorStoreProvider(builder.Configuration);
        builder.Services.AddScoped<IPostsQueryService, PostsQueryService>();
        builder.Services.AddScoped<IPostIndexingService, PostIndexingService>();
        builder.Services.AddScoped<ISemanticSearchService, SemanticSearchService>();
        builder.Services.AddScoped<IAgentOrchestrationService, AgentOrchestrationService>();
        builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        builder.Services.AddSingleton<IngestionTracker>();
        builder.Services.AddHostedService<IndexingStartupService>();
        builder.Services.AddHostedService<IngestionBackgroundService>();

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

        var app = builder.Build();

        await app.InitializeVectorStoreAsync();

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
        await app.RunAsync();
    }
}
