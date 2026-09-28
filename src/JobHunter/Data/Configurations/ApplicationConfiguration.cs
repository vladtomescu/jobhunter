using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunter.Data.Configurations;

/// <summary>Maps the application aggregate: the status columns plus the owned history, notes, contact, kit and cover letter as JSON.</summary>
public sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("Applications");
        builder.HasKey(application => application.Id);
        builder.HasIndex(application => application.JobId).IsUnique();

        builder.Property(application => application.Id).ValueGeneratedNever();

        builder.OwnsMany(application => application.History, history => history.ToJson());
        builder.OwnsMany(application => application.Notes, notes => notes.ToJson());
        builder.OwnsOne(application => application.Contact, contact => contact.ToJson());
        builder.OwnsOne(application => application.Kit, kit =>
        {
            kit.ToJson();
            kit.PrimitiveCollection(applicationKit => applicationKit.FitSummary);
            kit.PrimitiveCollection(applicationKit => applicationKit.CallQuestions);
            kit.PrimitiveCollection(applicationKit => applicationKit.LintIssues);
            kit.OwnsMany(applicationKit => applicationKit.AtsAnswers);
        });
        builder.OwnsOne(application => application.CoverLetter, coverLetter =>
        {
            coverLetter.ToJson();
            coverLetter.PrimitiveCollection(letter => letter.Paragraphs);
            coverLetter.PrimitiveCollection(letter => letter.LintIssues);
        });
    }
}
