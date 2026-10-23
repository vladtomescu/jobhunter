using JobHunter.Components.Shared;

namespace JobHunter.Tests.Components;

/// <summary>Proves that the loading bar stays on while any load or page change runs, and goes off once every one of them has ended, a failed load included.</summary>
public sealed class PageLoadingTests
{
    [Fact]
    public async Task RunAsync_WithTwoLoadsRunning_KeepsTheBarOnUntilBothEnd()
    {
        PageLoading loading = new();
        TaskCompletionSource<int> first = new();
        TaskCompletionSource<int> second = new();

        Task<int> firstRun = loading.RunAsync(() => first.Task);
        Task<int> secondRun = loading.RunAsync(() => second.Task);
        first.SetResult(1);
        Assert.Equal(1, await firstRun);
        Assert.True(loading.IsLoading);

        second.SetResult(2);
        Assert.Equal(2, await secondRun);
        Assert.False(loading.IsLoading);
    }

    [Fact]
    public async Task RunAsync_WhenTheLoadFails_TurnsTheBarOff()
    {
        PageLoading loading = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() => loading.RunAsync<int>(() => throw new InvalidOperationException()));

        Assert.False(loading.IsLoading);
    }

    [Fact]
    public async Task StartNavigationAsync_UntilTheNewPageArrives_KeepsTheBarOn()
    {
        PageLoading loading = new();
        int changes = 0;
        loading.Changed += () => changes++;

        await loading.StartNavigationAsync();
        Assert.True(loading.IsLoading);

        loading.EndNavigation();
        loading.EndNavigation();
        Assert.False(loading.IsLoading);
        Assert.Equal(2, changes);
    }
}
