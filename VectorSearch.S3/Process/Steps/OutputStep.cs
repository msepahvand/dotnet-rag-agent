using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Process;
using VectorSearch.Core.Models;

namespace VectorSearch.S3.Process.Steps;

/// <summary>
/// Terminal step. Writes the final <see cref="AgentAnswerResult"/> to the scoped
/// <see cref="ProcessResultHolder"/> so that <see cref="ProcessAnswerService"/> can return it
/// after the process completes.
/// </summary>
public sealed class OutputStep : KernelProcessStep
{
    [KernelFunction]
    public Task CaptureAsync(
        KernelProcessStepContext context,
        Kernel kernel,
        AgentAnswerResult answer)
    {
        var holder = kernel.Services.GetRequiredService<ProcessResultHolder>();
        holder.Result = answer;
        return Task.CompletedTask;
    }
}
