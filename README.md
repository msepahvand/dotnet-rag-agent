# .NET Vector Search with Multiple Providers

A comprehensive .NET 8.0 solution demonstrating semantic search using vector embeddings with support for multiple vector store providers: **Redis Stack**, **Qdrant**, and **AWS S3 Vectors**.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Vector Store Providers](#vector-store-providers)
- [Configuration](#configuration)
- [API Endpoints](#api-endpoints)
- [Docker & Local Development](#docker--local-development)
- [Integration Testing](#integration-testing)
- [Deployment](#deployment)
- [AWS S3 Vectors Setup](#aws-s3-vectors-setup)
- [Development Guide](#development-guide)
- [Troubleshooting](#troubleshooting)

---

## Overview

This project demonstrates production-ready semantic search implementation using:
- **Data Source**: JSONPlaceholder API (100 free posts for testing)
- **Embeddings**: AWS Bedrock Titan Embed Text v2 (1024 dimensions)
- **Vector Stores**: Redis Stack, Qdrant, or AWS S3 Vectors
- **Framework**: ASP.NET Core 8.0 Minimal APIs

### Project Structure

```
dotnet-vector-search/
├── VectorSearch.Core/           # Shared abstractions & models
│   ├── IVectorStore.cs         # Vector storage interface
│   ├── IEmbeddingService.cs    # Embedding generation interface
│   ├── IPostService.cs         # Data retrieval interface
│   ├── IVectorService.cs       # Main service interface
│   └── Post.cs                 # Shared models
├── VectorSearch.S3/             # AWS & Qdrant implementations
│   ├── EmbeddingService.cs     # Bedrock embedding service
│   ├── JsonPlaceholderService.cs
│   ├── QdrantVectorStore.cs    # Qdrant implementation
│   ├── S3VectorStore.cs        # S3 Vectors implementation
│   └── S3VectorService.cs      # Main service implementation
├── VectorSearch.Redis/          # Redis Stack implementation
│   └── RedisVectorStore.cs
├── VectorSearch.Api/            # Web API
│   └── Program.cs              # Minimal API endpoints
└── VectorSearch.IntegrationTests/
    └── VectorSearchIntegrationTests.cs  # 14 tests (7 × 2 providers)
```

---

## Features

### ✅ **Multi-Provider Support**
- **Redis Stack**: HNSW algorithm, RedisInsight UI, microsecond latency
- **Qdrant**: Dedicated vector DB, advanced filtering, dashboard UI
- **AWS S3 Vectors**: Managed service, auto-scaling, AWS integration

### ✅ **Production-Ready Architecture**
- Clean abstraction layer (IVectorStore interface)
- Dependency injection with Semantic Kernel
- Configuration-based provider selection
- Comprehensive error handling

### ✅ **Developer Experience**
- Docker Compose for local development
- Integration tests with Testcontainers
- No AWS credentials needed for local dev
- Visual UIs for debugging (RedisInsight, Qdrant Dashboard)

### ✅ **Enterprise Features**
- Batch indexing for efficiency
- Async/await throughout
- Health checks and initialization
- Semantic search with configurable topK

---

## Architecture

### Abstraction Layer

```
┌──────────────────────────────────┐
│      VectorSearch.Api            │
│      (Minimal APIs)              │
└────────────┬─────────────────────┘
             │
             ▼
┌──────────────────────────────────┐
│   IVectorService (Core)          │
│   ├─ IndexPostAsync              │
│   ├─ SemanticSearchAsync         │
│   └─ EnsureInitializedAsync      │
└────────────┬─────────────────────┘
             │
    ┌────────┴────────┐
    │                 │
    ▼                 ▼
┌─────────┐    ┌─────────────┐
│IEmbedding│   │IVectorStore │
│ Service │    │             │
└─────────┘    └──────┬──────┘
                      │
         ┌────────────┼────────────┐
         │            │            │
         ▼            ▼            ▼
   ┌────────┐   ┌────────┐  ┌──────────┐
   │ Redis  │   │Qdrant  │  │S3 Vectors│
   └────────┘   └────────┘  └──────────┘
```

### Dependency Flow

```
VectorSearch.Core (abstractions)
    ↑
    ├── VectorSearch.S3 (AWS + Qdrant implementations)
    ├── VectorSearch.Redis (Redis implementation)
    ├── VectorSearch.Api (Web API)
    └── VectorSearch.IntegrationTests
```

---

## Quick Start

### Prerequisites

- .NET 8.0 SDK
- Docker Desktop (for local development)
- AWS Account (only for S3 Vectors provider)

### 1. Clone & Build

```powershell
git clone https://github.com/mohammad-sepahvand_xero/dotnet-vector-search.git
cd dotnet-vector-search
dotnet build
```

### 2. Start with Docker Compose

```powershell
# Start all services (Redis, Qdrant, API)
docker-compose up

# Or start specific services
docker-compose up redis    # Redis Stack only
docker-compose up qdrant   # Qdrant only
```

### 3. Test the API

```powershell
# Get posts from JSONPlaceholder
curl http://localhost:5000/api/posts

# Index a single post
curl -X POST http://localhost:5000/api/index/1

# Search (using mock embeddings in dev)
curl "http://localhost:5000/api/search?query=test&topK=5"
```

### 4. Run Integration Tests

```powershell
cd VectorSearch.IntegrationTests
dotnet test
```

**Test Results**: 14 tests (7 tests × Redis and Qdrant providers)

---

## Vector Store Providers

### 1. Redis Stack (Recommended for Local Development)

**Best for**: Low-latency caching + vector search, local development

```bash
# Start Redis Stack
docker-compose up redis

# Access RedisInsight UI
http://localhost:8001
```

**Features:**
- HNSW algorithm for fast search
- COSINE distance metric
- RedisInsight visual UI
- Microsecond latency
- In-memory performance

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "Redis",
    "Redis": {
      "ConnectionString": "localhost:6379",
      "IndexName": "posts_idx",
      "VectorSize": "1024"
    }
  }
}
```

### 2. Qdrant (Recommended for Testing)

**Best for**: Dedicated vector database, integration testing

```bash
# Start Qdrant
docker-compose up qdrant

# Access Qdrant Dashboard
http://localhost:6333/dashboard
```

**Features:**
- Purpose-built vector database
- Advanced filtering and payloads
- Testcontainers support for tests
- REST and gRPC APIs
- Dashboard UI

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "Qdrant",
    "Qdrant": {
      "Url": "http://localhost:6333",
      "CollectionName": "posts",
      "VectorSize": 1024
    }
  }
}
```

### 3. AWS S3 Vectors (Recommended for Production)

**Best for**: Production AWS deployments, managed infrastructure

**Features:**
- Fully managed by AWS
- Auto-scaling
- AWS security and compliance
- S3 integration
- Predictable pricing

**Setup Required:**
1. AWS Account with Bedrock access
2. S3 Vector Bucket created
3. Bedrock model access enabled
4. AWS credentials configured

**Configuration:**
```json
{
  "VectorStore": {
    "Provider": "S3Vectors"
  },
  "AWS": {
    "Region": "us-east-1",
    "VectorBucketName": "your-bucket-name",
    "VectorIndexName": "your-index-name",
    "EmbeddingModelId": "amazon.titan-embed-text-v2:0"
  }
}
```

### Comparison Matrix

| Feature | Redis | Qdrant | S3 Vectors |
|---------|-------|--------|------------|
| **Deployment** | Docker/Cloud | Docker/Cloud | AWS Managed |
| **Algorithm** | HNSW/FLAT | HNSW | Proprietary |
| **UI** | RedisInsight (8001) | Dashboard (6333) | AWS Console |
| **Best Use Case** | Caching + Search | Dedicated Vector DB | AWS Production |
| **Latency** | Microseconds | Milliseconds | Milliseconds |
| **Scalability** | Vertical | Horizontal | Auto-scale |
| **Cost** | Self-hosted | Self-hosted | Pay-per-use |
| **Setup** | Docker only | Docker only | AWS account + config |

---

## Configuration

### Environment-Based Configuration

The application uses different configuration files for each environment:

- `appsettings.json` - Production (S3 Vectors)
- `appsettings.Development.json` - Local development (Qdrant)
- `appsettings.Redis.json` - Redis Stack

### Switch Providers via Environment Variable

```powershell
# Redis
$env:VectorStore__Provider="Redis"
$env:VectorStore__Redis__ConnectionString="localhost:6379"

# Qdrant
$env:VectorStore__Provider="Qdrant"
$env:VectorStore__Qdrant__Url="http://localhost:6333"

# S3 Vectors
$env:VectorStore__Provider="S3Vectors"
$env:AWS__VectorBucketName="my-bucket"
```

### AWS Credentials (S3 Vectors only)

```powershell
# Option 1: AWS CLI
aws configure

# Option 2: Environment Variables
$env:AWS_ACCESS_KEY_ID="your-key"
$env:AWS_SECRET_ACCESS_KEY="your-secret"
$env:AWS_REGION="us-east-1"
```

---

## API Endpoints

### Data Retrieval

```http
GET /api/posts
```
Fetches all 100 posts from JSONPlaceholder API.

```http
GET /api/posts/{id}
```
Fetches a specific post by ID.

### Indexing

```http
POST /api/index/all
```
Indexes all 100 posts with embeddings. **Note**: Requires AWS Bedrock access in production.

```http
POST /api/index/{id}
```
Indexes a single post by ID.

### Semantic Search

```http
GET /api/search?query=<text>&topK=<number>
```

**Parameters:**
- `query` (required): Search query text
- `topK` (optional): Number of results to return (default: 10)

**Example:**
```powershell
curl "http://localhost:5000/api/search?query=user%20interface&topK=5"
```

**Response:**
```json
{
  "query": "user interface",
  "topK": 5,
  "results": [
    {
      "distance": 0.85,
      "title": "Post about UI design",
      "postId": 42,
      "userId": 5
    }
  ]
}
```

---

## Docker & Local Development

### Docker Compose Services

The `docker-compose.yml` includes:

1. **Redis Stack** - Port 6379 (Redis) + 8001 (RedisInsight)
2. **Qdrant** - Port 6333 (HTTP API) + 6334 (gRPC)
3. **API** - Port 5000 (HTTP)

### Start All Services

```powershell
docker-compose up
```

### Start Specific Services

```powershell
# Redis only
docker-compose up redis

# Qdrant + API
docker-compose up qdrant api

# Stop all
docker-compose down
```

### Access Visual UIs

- **RedisInsight**: http://localhost:8001
- **Qdrant Dashboard**: http://localhost:6333/dashboard
- **API Swagger** (if enabled): http://localhost:5000/swagger

### Local Development Workflow

```powershell
# 1. Start vector database
docker-compose up redis  # or qdrant

# 2. Run API locally
cd VectorSearch.Api
dotnet run

# 3. Test endpoints
curl http://localhost:5000/api/posts

# 4. Run tests
cd ../VectorSearch.IntegrationTests
dotnet test
```

---

## Integration Testing

### Test Infrastructure

Uses:
- **xUnit** - Test framework
- **Testcontainers** - Automatic container management
- **WebApplicationFactory** - In-memory API testing
- **Theory-based tests** - Parameterized tests for multiple providers

### Run All Tests

```powershell
cd VectorSearch.IntegrationTests
dotnet test
```

**Output**: 14 tests passing (7 tests × 2 providers: Qdrant + Redis)

### Test Coverage

| Test | What It Validates |
|------|-------------------|
| `GetPosts_ReturnsSuccessAndPosts` | JSONPlaceholder API integration |
| `GetPost_WithValidId_ReturnsPost` | Single post retrieval |
| `GetPost_WithInvalidId_ReturnsNotFound` | Error handling |
| `IndexSinglePost_ThenSearch_ReturnsPost` | End-to-end indexing + search |
| `IndexMultiplePosts_ThenSearch_ReturnsRelevantResults` | Batch indexing + relevance |
| `Search_WithNoIndexedData_ReturnsEmptyResults` | Empty state handling |
| `Search_WithEmptyQuery_ReturnsBadRequest` | Input validation |

### Run Specific Tests

```powershell
# Single test
dotnet test --filter "GetPosts_ReturnsSuccessAndPosts"

# Specific provider
dotnet test --filter "provider=Redis"

# Verbose output
dotnet test --logger "console;verbosity=detailed"
```

### CI/CD Integration

Tests are CI/CD-friendly and work in:
- GitHub Actions
- Azure DevOps
- GitLab CI
- Jenkins

Example GitHub Actions workflow:

```yaml
- name: Run Integration Tests
  run: dotnet test
  working-directory: ./VectorSearch.IntegrationTests
```

---

## Deployment

### Deploy to Azure App Service

```powershell
# Publish the API
dotnet publish -c Release -o ./publish

# Deploy to Azure (configure provider in App Settings)
az webapp up --name your-app-name --resource-group your-rg
```

**App Settings:**
- `VectorStore__Provider` = "Redis" or "S3Vectors"
- Add Redis connection string or AWS credentials

### Deploy to AWS

```powershell
# Use S3 Vectors provider
dotnet publish -c Release

# Deploy to Elastic Beanstalk or ECS
# Configure AWS credentials via IAM role
```

### Docker Production Build

```dockerfile
# Production-ready container
docker build -t vectorsearch-api -f VectorSearch.Api/Dockerfile .
docker run -p 8080:8080 vectorsearch-api
```

---

## AWS S3 Vectors Setup

### Prerequisites

1. **AWS Account** with access to:
   - Amazon Bedrock
   - S3 Vectors

### Step 1: Create S3 Vector Bucket

Via AWS Console:
1. Navigate to S3 service
2. Create new **S3 Vector Bucket** (not regular S3!)
3. Set bucket name: `posts-semantic-search`
4. Create vector index:
   - Name: `posts-content-index`
   - Dimensions: **1024** (for Titan v2 embeddings)
   - Distance metric: **Cosine similarity**

### Step 2: Enable Bedrock Model Access

1. Go to Amazon Bedrock console
2. Navigate to "Model access"
3. Enable `amazon.titan-embed-text-v2:0`
4. Wait for access to be granted (usually < 1 minute)

### Step 3: Configure AWS Credentials

```powershell
aws configure
# Enter Access Key ID
# Enter Secret Access Key
# Enter Region (e.g., us-east-1)
```

### Step 4: Update Configuration

Edit `appsettings.json`:

```json
{
  "VectorStore": {
    "Provider": "S3Vectors"
  },
  "AWS": {
    "Region": "us-east-1",
    "VectorBucketName": "posts-semantic-search",
    "VectorIndexName": "posts-content-index",
    "EmbeddingModelId": "amazon.titan-embed-text-v2:0"
  }
}
```

### Step 5: Test

```powershell
dotnet run --project VectorSearch.Api

# Index posts (will incur small AWS costs)
curl -X POST http://localhost:5000/api/index/all

# Search
curl "http://localhost:5000/api/search?query=technology&topK=10"
```

### Cost Considerations

- **Bedrock Embeddings**: ~$0.0001 per 1K tokens
- **S3 Vectors**: Storage + query costs (preview pricing)
- **100 posts**: Approximately $0.01 to index

---

## Development Guide

### Adding a New Vector Store Provider

1. Implement `IVectorStore` interface in new project
2. Add project reference to `VectorSearch.Core`
3. Register in `ServiceCollectionExtensions.cs`
4. Add configuration section
5. Add integration tests

Example:

```csharp
public class PineconeVectorStore : IVectorStore
{
    public async Task IndexDocumentAsync(string key, float[] embedding, 
        Dictionary<string, string> metadata)
    {
        // Implementation
    }
    // ... other methods
}
```

### Using Semantic Kernel

The project uses **Microsoft.SemanticKernel** for:
- AWS Bedrock integration
- Embedding generation via `IEmbeddingGenerator<string, Embedding<float>>`
- Dependency injection with `Kernel`

Located in `VectorSearch.S3/EmbeddingService.cs`:

```csharp
public class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public EmbeddingService(Kernel kernel)
    {
        _embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var result = await _embeddingGenerator.GenerateAsync([text]);
        return result[0].Vector.ToArray();
    }
}
```

### Mock Embedding Service

For local testing without AWS credentials, use `MockEmbeddingService`:

```csharp
public class MockEmbeddingService : IEmbeddingService
{
    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Returns deterministic embeddings based on text hash
        return Task.FromResult(GenerateMockEmbedding(text));
    }
}
```

---

## Troubleshooting

### Docker Issues

**Problem**: Cannot connect to Docker

```powershell
# Solution: Ensure Docker Desktop is running
docker ps
```

**Problem**: Port already in use

```powershell
# Solution: Stop conflicting services
docker-compose down
# Or change ports in docker-compose.yml
```

### Build Warnings

**Warning**: `ITextEmbeddingGenerationService` is obsolete

**Solution**: Safe to ignore. The code uses the newer `IEmbeddingGenerator` interface but legacy references remain for compatibility.

**Warning**: Testcontainers obsolete constructors

**Solution**: Functionality works correctly. Update to `.WithImage()` constructors when upgrading Testcontainers.

### Test Failures

**Problem**: Tests timing out

```powershell
# Solution: Check Docker resources (memory, CPU)
# Increase timeout in test code if needed
```

**Problem**: Container startup failures

```powershell
# Solution: Pull images manually
docker pull qdrant/qdrant:latest
docker pull redis/redis-stack:latest
```

### AWS Errors

**Error**: "Access Denied"

**Solution**:
- Check AWS credentials: `aws sts get-caller-identity`
- Verify IAM permissions for Bedrock and S3 Vectors
- Ensure model access is enabled in Bedrock console

**Error**: "Model not found"

**Solution**:
- Go to Bedrock console → Model access
- Enable `amazon.titan-embed-text-v2:0`
- Wait for access to be granted

**Error**: "Bucket not found"

**Solution**:
- Verify bucket name in `appsettings.json`
- Ensure it's an S3 **Vector Bucket** (not regular S3)
- Check AWS region matches configuration

### No Search Results

**Problem**: Search returns empty results

**Solutions**:
1. Ensure posts are indexed: `POST /api/index/all`
2. Check console logs for indexing errors
3. Verify vector dimensions match (1024)
4. For S3 Vectors: Allow time for indexing to complete (~1-2 minutes)
5. For Redis/Qdrant: Check collection/index exists via UI

---

## Resources

### Documentation

- [AWS S3 Vectors](https://docs.aws.amazon.com/AmazonS3/latest/userguide/s3-vectors.html)
- [Amazon Bedrock](https://docs.aws.amazon.com/bedrock/)
- [Redis Vector Search](https://redis.io/docs/interact/search-and-query/advanced-concepts/vectors/)
- [Qdrant Documentation](https://qdrant.tech/documentation/)
- [Semantic Kernel](https://learn.microsoft.com/en-us/semantic-kernel/)
- [Testcontainers .NET](https://dotnet.testcontainers.org/)

### Related Projects

- [NRedisStack](https://github.com/redis/NRedisStack) - Official Redis Stack .NET client
- [Qdrant .NET Client](https://github.com/qdrant/qdrant-dotnet)
- [JSONPlaceholder](https://jsonplaceholder.typicode.com/) - Free fake API

---

## License

MIT License

---

## Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Ensure all tests pass
5. Submit a pull request

---

## Support

For issues or questions:
- Open an issue on GitHub
- Check existing issues and documentation
- Review troubleshooting section above

---

**Happy Vector Searching! 🚀**