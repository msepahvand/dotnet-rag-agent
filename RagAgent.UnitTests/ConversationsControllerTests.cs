using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RagAgent.Api.Controllers;
using RagAgent.InMemory;

namespace RagAgent.UnitTests;

public class ConversationsControllerTests
{
    [Fact]
    public async Task ListConversationsAsync_WhenListingDisabled_ReturnsNotFoundAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Conversations:ListEnabled"] = "false"
            })
            .Build();
        var sut = new ConversationsController(
            new InMemoryConversationStore(new MemoryCache(new MemoryCacheOptions())),
            configuration);

        var result = await sut.ListConversationsAsync();

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task ListConversationsAsync_WhenListingSettingIsAbsent_PreservesExistingBehaviourAsync()
    {
        var store = new InMemoryConversationStore(new MemoryCache(new MemoryCacheOptions()));
        await store.AppendAsync("conversation-1", new RagAgent.Core.Models.ConversationMessage("user", "hello"));
        var sut = new ConversationsController(store, new ConfigurationBuilder().Build());

        var result = await sut.ListConversationsAsync();

        result.Should().BeOfType<OkObjectResult>();
    }
}
