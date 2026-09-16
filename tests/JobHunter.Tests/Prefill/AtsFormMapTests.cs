using JobHunter.Domain;
using JobHunter.Prefill;

namespace JobHunter.Tests.Prefill;

public class AtsFormMapTests
{
    [Theory]
    [InlineData("https://job-boards.greenhouse.io/everlaw/jobs/4705236006", AtsKind.Greenhouse)]
    [InlineData("https://job-boards.eu.greenhouse.io/someorg/jobs/1234567", AtsKind.Greenhouse)]
    [InlineData("https://boards.greenhouse.io/someorg/jobs/1234567", AtsKind.Greenhouse)]
    [InlineData("https://jobs.lever.co/abovelending/b6f9c643/apply", AtsKind.Lever)]
    [InlineData("https://jobs.ashbyhq.com/1password/6f78b170/application", AtsKind.Ashby)]
    public void ForUrl_MappedHost_ReturnsMapOfThatSystem(string url, AtsKind expected)
    {
        AtsFormMap? map = AtsFormMap.ForUrl(url);

        Assert.NotNull(map);
        Assert.Equal(expected, map.Ats);
    }

    [Theory]
    [InlineData("https://weworkremotely.com/remote-jobs/some-company-backend-engineer")]
    [InlineData("https://remoteok.com/remote-jobs/123456")]
    [InlineData("https://apply.workable.com/someorg/j/ABCDEF/")]
    [InlineData("https://jobs.smartrecruiters.com/someorg/1234567")]
    [InlineData("https://careers.example.com/openings/42")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void ForUrl_UnmappedHost_ReturnsNoMapSoThePostingIsOnlyOpened(string? url)
    {
        Assert.Null(AtsFormMap.ForUrl(url));
    }

    [Fact]
    public void ForUrl_HostDecidesRatherThanTheSystemOnTheJob_ReturnsNoMapForAJobBoardLink()
    {
        Assert.Null(AtsFormMap.ForUrl("https://weworkremotely.com/remote-jobs/greenhouse-sourced-posting"));
    }

    [Fact]
    public void All_EverySystem_IsMappedExactlyOnce()
    {
        Assert.Equal<AtsKind>([AtsKind.Greenhouse, AtsKind.Lever, AtsKind.Ashby], AtsFormMap.All.Select(map => map.Ats).ToArray());
    }

    [Fact]
    public void All_EveryMap_CarriesAReadySelectorThatIsAlsoASelectorOfAField()
    {
        foreach (AtsFormMap map in AtsFormMap.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(map.ReadySelector));
            Assert.Contains(map.Selectors.Values.SelectMany(selectors => selectors), selector => selector == map.ReadySelector);
        }
    }

    [Fact]
    public void All_EveryMap_ReachesNameEmailPhoneLinkedInAndResume()
    {
        foreach (AtsFormMap map in AtsFormMap.All)
        {
            bool names = map.SelectorsFor(PrefillField.FullName).Count > 0
                || (map.SelectorsFor(PrefillField.FirstName).Count > 0 && map.SelectorsFor(PrefillField.LastName).Count > 0);

            Assert.True(names, $"{map.Ats} maps no name field.");
            Assert.NotEmpty(map.SelectorsFor(PrefillField.Email));
            Assert.NotEmpty(map.SelectorsFor(PrefillField.Phone));
            Assert.NotEmpty(map.SelectorsFor(PrefillField.LinkedIn));
            Assert.NotEmpty(map.SelectorsFor(PrefillField.Resume));
        }
    }

    [Fact]
    public void SelectorsFor_FieldTheSystemDoesNotHave_IsEmptySoNothingIsTyped()
    {
        Assert.Empty(GreenhouseFormMap.Map.SelectorsFor(PrefillField.FullName));
        Assert.Empty(LeverFormMap.Map.SelectorsFor(PrefillField.FirstName));
        Assert.Empty(AshbyFormMap.Map.SelectorsFor(PrefillField.LastName));
    }

    [Fact]
    public void All_EverySelector_IsNonEmptyAndTrimmed()
    {
        foreach (string selector in AtsFormMap.All.SelectMany(map => map.Selectors.Values).SelectMany(selectors => selectors))
        {
            Assert.False(string.IsNullOrWhiteSpace(selector));
            Assert.Equal(selector.Trim(), selector);
        }
    }
}
