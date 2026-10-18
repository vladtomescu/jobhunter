using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Data.Configurations;

/// <summary>Maps the skill catalog: a name unique regardless of case, and the aliases as a JSON list; that one alias names one skill is kept by the skill catalog, not by the database.</summary>
public sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("Skills");
        builder.HasKey(skill => skill.Id);
        builder.HasIndex(skill => skill.Name).IsUnique();

        builder.Property(skill => skill.Name).IsRequired().UseCollation("NOCASE");

        builder.PrimitiveCollection(skill => skill.Aliases);
    }
}
