using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the skill catalog resolves a raw skill name through the skill's name and aliases whatever the case and the surrounding spaces, and that one name never resolves to two skills.</summary>
public sealed class SkillCatalogTests
{
    [Theory]
    [InlineData("Go")]
    [InlineData("go")]
    [InlineData(" golang ")]
    [InlineData("GoLang")]
    public void Resolve_ForTheNameOrAnAliasInAnyCaseAndSpacing_ReturnsTheSkill(string rawName)
    {
        Skill go = NewSkill("Go", "golang");
        SkillCatalog catalog = new([go, NewSkill("Kubernetes", "k8s")]);

        Assert.Same(go, catalog.Resolve(rawName));
    }

    [Fact]
    public void Resolve_ForANameNoSkillCarries_ReturnsNull()
    {
        SkillCatalog catalog = new([NewSkill("Go", "golang")]);

        Assert.Null(catalog.Resolve("Rust"));
    }

    [Fact]
    public void Constructor_WithTwoSkillsSharingAnAlias_RefusesTheCatalog()
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => new SkillCatalog([NewSkill("PostgreSQL", "postgres"), NewSkill("Postgres")]));

        Assert.Contains("postgres", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_ForASkillWhoseAliasAnotherSkillHolds_AddsNothing()
    {
        Skill kubernetes = NewSkill("Kubernetes", "k8s");
        SkillCatalog catalog = new([kubernetes]);

        Assert.Throws<InvalidOperationException>(() => catalog.Add(NewSkill("Helm", "k8s")));

        Assert.Null(catalog.Resolve("helm"));
        Assert.Same(kubernetes, catalog.Resolve("k8s"));
    }

    [Fact]
    public void Add_ForANewSkill_ResolvesItByItsName()
    {
        SkillCatalog catalog = new([]);
        Skill terraform = Skill.CreateFromModel(" Terraform ", SkillCategory.Tool, CvMark.No);

        catalog.Add(terraform);

        Assert.Same(terraform, catalog.Resolve("TERRAFORM"));
        Assert.Equal("Terraform", terraform.Name);
    }

    private static Skill NewSkill(string name, params string[] aliases)
    {
        Skill skill = Skill.CreateFromModel(name, SkillCategory.Unsorted, CvMark.Yes);
        skill.Aliases.AddRange(aliases);

        return skill;
    }
}
