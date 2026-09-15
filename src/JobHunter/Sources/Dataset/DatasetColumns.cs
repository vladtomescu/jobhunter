namespace JobHunter.Sources.Dataset;

/// <summary>The dataset columns the reader asks for; the slices carry more columns, and everything not named here is never decoded.</summary>
internal static class DatasetColumns
{
    internal const string Url = "url";

    internal const string Title = "title";

    internal const string Company = "company";

    internal const string AtsType = "ats_type";

    internal const string AtsId = "ats_id";

    internal const string Location = "location";

    internal const string CountryIso = "country_iso";

    internal const string Region = "region";

    internal const string Language = "language";

    internal const string IsRemote = "is_remote";

    internal const string SalaryMin = "salary_min";

    internal const string SalaryMax = "salary_max";

    internal const string SalaryCurrency = "salary_currency";

    internal const string SalaryPeriod = "salary_period";

    internal const string EmploymentType = "employment_type";

    internal const string Department = "department";

    internal const string Description = "description";

    internal const string PostedAt = "posted_at";

    internal const string ApplyUrl = "apply_url";
}
