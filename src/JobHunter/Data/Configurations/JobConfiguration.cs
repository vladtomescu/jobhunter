using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Data.Configurations;

/// <summary>Maps the job aggregate: scalar columns, the tag and flag collections, and the owned source list and score card as JSON.</summary>
public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs");
        builder.HasKey(job => job.Id);
        builder.HasIndex(job => job.Fingerprint).IsUnique();

        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.Fingerprint).IsRequired();
        builder.Property(job => job.CanonicalApplyUrl).IsRequired();
        builder.Property(job => job.PostingUrl).IsRequired();
        builder.Property(job => job.Company).IsRequired();
        builder.Property(job => job.Title).IsRequired();
        builder.Property(job => job.DescriptionText).IsRequired();
        builder.Property(job => job.DescriptionHash).IsRequired();

        builder.PrimitiveCollection(job => job.Tags);
        builder.PrimitiveCollection(job => job.Flags).ElementType(element => element.HasConversion<string>());

        builder.OwnsMany(job => job.Sources, sources => sources.ToJson());
        builder.OwnsOne(job => job.Score, score =>
        {
            score.ToJson();
            score.PrimitiveCollection(card => card.BlockingUnknowns);
        });
    }
}
