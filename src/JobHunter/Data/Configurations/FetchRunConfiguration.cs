using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Data.Configurations;

/// <summary>Maps the refresh run record with its per-source results as JSON.</summary>
public sealed class FetchRunConfiguration : IEntityTypeConfiguration<FetchRun>
{
    public void Configure(EntityTypeBuilder<FetchRun> builder)
    {
        builder.ToTable("FetchRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.HasIndex(run => run.StartedAt);

        builder.OwnsMany(run => run.SourceResults, results => results.ToJson());
    }
}
