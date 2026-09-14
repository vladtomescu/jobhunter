using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobHunter.Data;

/// <summary>The one database context: jobs, applications, the settings row and the refresh runs.</summary>
public sealed class JobHunterDbContext : DbContext
{
    /// <summary>Creates the context; instances come from the context factory, never from a constructor call in application code.</summary>
    public JobHunterDbContext(DbContextOptions<JobHunterDbContext> options)
        : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<Domain.Settings> Settings => Set<Domain.Settings>();

    public DbSet<FetchRun> FetchRuns => Set<FetchRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobHunterDbContext).Assembly);

        ApplyStorageConversions(modelBuilder);
    }

    /// <summary>Gives every enum a text column, every decimal a REAL column, and every timestamp outside a JSON document fixed-width UTC text; the values inside JSON documents stay with the serializer.</summary>
    private static void ApplyStorageConversions(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            bool isJsonDocument = entityType.IsMappedToJson();

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                Type propertyType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                if (propertyType.IsEnum)
                {
                    property.SetValueConverter(EnumToTextConverter(propertyType));
                }
                else if (propertyType == typeof(decimal))
                {
                    property.SetValueConverter(SqliteStorageConversions.DecimalToDouble);
                }
                else if (propertyType == typeof(DateTimeOffset) && !isJsonDocument)
                {
                    property.SetValueConverter(SqliteStorageConversions.DateTimeOffsetToUtcText);
                }
            }
        }
    }

    private static ValueConverter EnumToTextConverter(Type enumType)
    {
        Type converterType = typeof(EnumToStringConverter<>).MakeGenericType(enumType);

        return (ValueConverter)Activator.CreateInstance(converterType)!;
    }
}
