using Guara.Abstractions;
using Guara.Core;
using Xunit;

namespace Guara.Core.Tests;

public class JobStateMachineTests
{
    private readonly JobStateMachine _sm = new();

    [Theory]
    [InlineData(JobState.Created, JobState.Enqueued)]
    [InlineData(JobState.Created, JobState.Scheduled)]
    [InlineData(JobState.Enqueued, JobState.Processing)]
    [InlineData(JobState.Processing, JobState.Succeeded)]
    [InlineData(JobState.Processing, JobState.Failed)]
    [InlineData(JobState.Processing, JobState.Retrying)]
    [InlineData(JobState.Processing, JobState.Scheduled)]
    [InlineData(JobState.Retrying, JobState.Enqueued)]
    public void ValidTransitions_Allowed(JobState from, JobState to)
        => Assert.True(_sm.CanTransition(from, to));

    [Theory]
    [InlineData(JobState.Created, JobState.Succeeded)]
    [InlineData(JobState.Succeeded, JobState.Processing)]
    [InlineData(JobState.Enqueued, JobState.Succeeded)]
    public void InvalidTransitions_Rejected(JobState from, JobState to)
    {
        Assert.False(_sm.CanTransition(from, to));
        Assert.Throws<InvalidOperationException>(() => _sm.Transition(from, to));
    }
}
