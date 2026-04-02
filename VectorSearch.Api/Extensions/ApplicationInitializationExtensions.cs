using VectorSearch.Core;

namespace VectorSearch.Api.Extensions;

public static class ApplicationInitializationExtensions
{
    public static async Task InitializeVectorStoreAsync(this WebApplication app)
    {
        var vectorProvider = app.Configuration["VectorStore:Provider"] ?? "S3Vectors";
        var initializeOnStartupOverride = app.Configuration.GetValue<bool?>("VectorStore:InitializeOnStartup");
        var requiresStartupInitialization =
            vectorProvider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase) ||
            vectorProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase);
        var shouldInitializeOnStartup = initializeOnStartupOverride ?? requiresStartupInitialization;

        if (app.Environment.IsEnvironment("Testing") || !shouldInitializeOnStartup)
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var vectorService = scope.ServiceProvider.GetRequiredService<IVectorService>();
        try
        {
            await vectorService.EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Vector store initialisation failed at startup. Service will continue running.");
        }
    }
}
