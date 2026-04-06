using Amazon.S3Vectors;
using Microsoft.Extensions.DependencyInjection;
using RagAgent.Core;

namespace RagAgent.S3Vectors;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddS3VectorsVectorStore(this IServiceCollection services)
    {
        services.AddAWSService<IAmazonS3Vectors>();
        services.AddScoped<IVectorStore, S3VectorStore>();
        return services;
    }
}
