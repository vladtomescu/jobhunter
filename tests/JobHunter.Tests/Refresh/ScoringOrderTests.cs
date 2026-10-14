using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Refresh;
using JobHunter.Sources;
using JobHunter.Tests.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves the one order jobs are scored in: the jobs added by hand, then the jobs that need no relocation, then the ones that do, each by stated yearly pay with unknown pay last, and ties to the newest; read through SQLite, as the Score button and the to-score endpoint read it.</summary>
public sealed class ScoringOrderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Apply_WithEveryTier_PutsManualJobsFirstThenTheJobsWithoutRelocationThenTheOnesWithIt()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job relocation = WaitingJob("Relocation Corp", [JobFlag.H4], maxPerYear: 200_000m);
        Job remote = WaitingJob("Remote Corp", [], maxPerYear: 60_000m);
        Job homeCity = WaitingJob("Home City Corp", [JobFlag.H4, JobFlag.HomeCity], maxPerYear: 50_000m);
        Job manual = WaitingJob("Manual Corp", [JobFlag.H4], isManual: true);
        await SaveAsync(harness, relocation, remote, homeCity, manual);

        List<Guid> order = await ReadOrderAsync(harness);

        Assert.Equal<Guid>([manual.Id, remote.Id, homeCity.Id, relocation.Id], order);
    }

    [Fact]
    public async Task Apply_InsideATier_PutsTheHighestStatedPayFirstAndUnknownPayLast()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job unknownPay = WaitingJob("Unknown Pay Corp", []);
        Job lowPay = WaitingJob("Low Pay Corp", [], maxPerYear: 40_000m);
        Job minimumOnly = WaitingJob("Minimum Only Corp", [], minPerYear: 90_000m);
        Job highPay = WaitingJob("High Pay Corp", [], minPerYear: 50_000m, maxPerYear: 120_000m);
        Job relocationUnknownPay = WaitingJob("Relocation Unknown Corp", [JobFlag.H4]);
        Job relocationPay = WaitingJob("Relocation Pay Corp", [JobFlag.H4], maxPerYear: 70_000m);
        await SaveAsync(harness, unknownPay, lowPay, minimumOnly, highPay, relocationUnknownPay, relocationPay);

        List<Guid> order = await ReadOrderAsync(harness);

        Assert.Equal<Guid>([highPay.Id, minimumOnly.Id, lowPay.Id, unknownPay.Id, relocationPay.Id, relocationUnknownPay.Id], order);
    }

    [Fact]
    public async Task Apply_WithEqualPay_PutsTheNewestFirstByPostingDateElseFirstSighting()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job postedLongAgo = WaitingJob("Old Posting Corp", [], maxPerYear: 80_000m, firstSeenAt: Noon, postedAt: Noon.AddDays(-10));
        Job seenYesterday = WaitingJob("Undated Corp", [], maxPerYear: 80_000m, firstSeenAt: Noon.AddDays(-1));
        Job postedToday = WaitingJob("New Posting Corp", [], maxPerYear: 80_000m, firstSeenAt: Noon.AddDays(-5), postedAt: Noon);
        await SaveAsync(harness, postedLongAgo, seenYesterday, postedToday);

        List<Guid> order = await ReadOrderAsync(harness);

        Assert.Equal<Guid>([postedToday.Id, seenYesterday.Id, postedLongAgo.Id], order);
    }

    [Fact]
    public async Task Apply_AmongManualJobs_PutsTheNewestFirstWhateverThePay()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job olderWellPaid = WaitingJob("Older Manual Corp", [], maxPerYear: 150_000m, firstSeenAt: Noon.AddDays(-2), isManual: true);
        Job newerUnpaid = WaitingJob("Newer Manual Corp", [], firstSeenAt: Noon, isManual: true);
        await SaveAsync(harness, olderWellPaid, newerUnpaid);

        List<Guid> order = await ReadOrderAsync(harness);

        Assert.Equal<Guid>([newerUnpaid.Id, olderWellPaid.Id], order);
    }

    [Fact]
    public async Task RunAsync_WithTheCapAtOne_ScoresOneJobPerRunInScoringOrder()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job relocation = WaitingJob("Relocation Corp", [JobFlag.H4], maxPerYear: 200_000m, postedAt: Noon);
        Job remoteUnknownPay = WaitingJob("Remote Unknown Corp", [], postedAt: Noon);
        Job remotePaid = WaitingJob("Remote Paid Corp", [], maxPerYear: 60_000m, postedAt: Noon.AddDays(-7));
        Job manual = WaitingJob("Manual Corp", [], firstSeenAt: Noon.AddDays(-3), isManual: true);
        await SaveAsync(harness, relocation, remoteUnknownPay, remotePaid, manual);
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 12, 1));

        for (int run = 0; run < 4; run++)
        {
            await harness.ScoreAsync();
        }

        Assert.Equal<Guid>([manual.Id, remotePaid.Id, remoteUnknownPay.Id, relocation.Id], [.. harness.Scorer.Requests.Select(request => request.JobId)]);
    }

    private static Job WaitingJob(string company, JobFlag[] flags, decimal? minPerYear = null, decimal? maxPerYear = null, DateTimeOffset? firstSeenAt = null, DateTimeOffset? postedAt = null, bool isManual = false)
    {
        Job job = ListedJobs.NewJob(company, "Backend Engineer", firstSeenAt ?? Noon, isManual ? JobSourceKind.Manual : JobSourceKind.RemoteOk, isManual);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, flags);

        if (postedAt is DateTimeOffset posted)
        {
            job.RecordPostingFacts(null, null, null, [], null, posted);
        }

        if (minPerYear is not null || maxPerYear is not null)
        {
            job.RecordCompensation(minPerYear, maxPerYear, "EUR", CompPeriod.Year, minPerYear, maxPerYear);
        }

        return job;
    }

    private static async Task SaveAsync(RefreshTestHarness harness, params Job[] jobs)
    {
        foreach (Job job in jobs)
        {
            await harness.SaveAsync(job);
        }
    }

    private static async Task<List<Guid>> ReadOrderAsync(RefreshTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await ScoringOrder.Apply(ScoreBacklog.Of(context).AsNoTracking()).Select(job => job.Id).ToListAsync();
    }
}
