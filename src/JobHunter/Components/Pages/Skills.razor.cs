using System.Globalization;
using JobHunter.Components.Shared;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;

namespace JobHunter.Components.Pages;

/// <summary>The skills page: every skill of the catalog with how often the scored postings ask for it and what they pay, the CV mark to edit, and the merge of one skill into another.</summary>
public partial class Skills
{
    private readonly SkillListFilter filter = new();
    private readonly GridSort<SkillRow> nameSort = GridSort<SkillRow>.ByAscending(row => row.Name);
    private readonly GridSort<SkillRow> shareSort = GridSort<SkillRow>.ByAscending(row => row.Share);
    private readonly GridSort<SkillRow> paySort = GridSort<SkillRow>.ByAscending(row => row.MedianPayPerYear);

    private SkillList? list;
    private IQueryable<SkillRow> rows = Array.Empty<SkillRow>().AsQueryable();
    private Domain.Settings settings = Domain.Settings.CreateDefault();
    private int? mergedSkillId;
    private int? targetSkillId;
    private bool isConfirmingMerge;
    private bool busy;
    private string? message;

    [Inject]
    private SkillQueryService SkillQueries { get; set; } = null!;

    [Inject]
    private SkillService SkillService { get; set; } = null!;

    [Inject]
    private JobHunter.Settings.SettingsService SettingsService { get; set; } = null!;

    [Inject]
    private PageLoading Loading { get; set; } = null!;

    /// <summary>The skills in name order, for the merge pickers.</summary>
    private IEnumerable<SkillRow> SkillsByName => (list?.Rows ?? []).OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase);

    private SkillRow? MergedSkill => list?.Rows.FirstOrDefault(row => row.Id == mergedSkillId);

    private SkillRow? TargetSkill => mergedSkillId == targetSkillId ? null : list?.Rows.FirstOrDefault(row => row.Id == targetSkillId);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        settings = await SettingsService.GetAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        list = await Loading.RunAsync(() => SkillQueries.GetSkillsAsync(filter, DateTimeOffset.UtcNow));
        rows = list.Rows.AsQueryable();
    }

    private async Task ClearAsync()
    {
        filter.Class = null;
        filter.PayFloorPerYear = null;
        filter.PostedWithinDays = null;
        filter.ExcludeUnitedStates = false;

        await LoadAsync();
    }

    private async Task SetCvMarkAsync(SkillRow skill, ChangeEventArgs change)
    {
        if (!Enum.TryParse(change.Value?.ToString(), out CvMark cvMark))
        {
            return;
        }

        await RunAsync(async () =>
        {
            int classesChanged = await SkillService.SetCvMarkAsync(skill.Id, cvMark);
            message = $"Marked {skill.Name} as {CvMarkLabel(cvMark)}; {classesChanged} job(s) changed class.";
        });
    }

    private void AskToConfirmMerge()
    {
        message = null;
        isConfirmingMerge = MergedSkill is not null && TargetSkill is not null;
    }

    private void CancelMerge()
    {
        isConfirmingMerge = false;
    }

    private async Task MergeAsync()
    {
        if (MergedSkill is not SkillRow merged || TargetSkill is not SkillRow target)
        {
            return;
        }

        await RunAsync(async () =>
        {
            int classesChanged = await SkillService.MergeAsync(merged.Id, target.Id);
            message = $"Merged {merged.Name} into {target.Name}; {classesChanged} job(s) changed class.";
            mergedSkillId = null;
            targetSkillId = null;
            isConfirmingMerge = false;
        });
    }

    /// <summary>Runs one change with the controls off, then reads the list again so the marks and the counts show what was saved.</summary>
    /// <remarks>A skill merged or deleted from another tab is no longer in the catalog, so the change is refused and the fresh list shows why.</remarks>
    private async Task RunAsync(Func<Task> change)
    {
        busy = true;

        try
        {
            await change();
        }
        catch (InvalidOperationException)
        {
            message = "That skill is no longer in the catalog; the list was read again.";
            isConfirmingMerge = false;
        }
        finally
        {
            busy = false;
        }

        await LoadAsync();
    }

    private static string CvMarkLabel(CvMark cvMark)
    {
        return cvMark switch
        {
            CvMark.Yes => "on the CV",
            CvMark.Close => "close",
            _ => "missing"
        };
    }

    private static string FormatShare(double share)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{share * 100:0.#}%");
    }
}
