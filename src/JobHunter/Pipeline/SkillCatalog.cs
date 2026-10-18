using JobHunter.Domain;

namespace JobHunter.Pipeline;

/// <summary>Every stored skill under each name that resolves to it, its own name and its aliases, lowercased and trimmed; it resolves the raw skill names on score cards to canonical skills.</summary>
/// <remarks>One name resolves to one skill: the catalog refuses a skill that claims a name another skill already holds, which keeps the aliases unique without a database constraint.</remarks>
public sealed class SkillCatalog
{
    private readonly Dictionary<string, Skill> skillsByName = new(StringComparer.Ordinal);

    /// <summary>Builds the catalog over the given skills; two skills sharing a name or an alias is an error.</summary>
    public SkillCatalog(IEnumerable<Skill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        foreach (Skill skill in skills)
        {
            Add(skill);
        }
    }

    /// <summary>The form a raw skill name is looked up in: trimmed and lowercased.</summary>
    public static string Normalize(string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);

        return rawName.Trim().ToLowerInvariant();
    }

    /// <summary>The skill the raw name resolves to, or null when no skill carries it as its name or one of its aliases.</summary>
    public Skill? Resolve(string rawName)
    {
        return skillsByName.GetValueOrDefault(Normalize(rawName));
    }

    /// <summary>The name a raw skill name counts under: the normalized name of the skill it resolves to, so every name of one skill counts once, or the raw name normalized when no skill carries it.</summary>
    public string KeyOf(string rawName)
    {
        return Normalize(Resolve(rawName)?.Name ?? rawName);
    }

    /// <summary>Adds a skill under its name and every alias; a name another skill already holds is refused and nothing is added.</summary>
    public void Add(Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        string[] names = [.. NamesOf(skill)];

        foreach (string name in names)
        {
            if (skillsByName.TryGetValue(name, out Skill? holder) && !ReferenceEquals(holder, skill))
            {
                throw new InvalidOperationException($"The name \"{name}\" of the skill {skill.Name} already resolves to the skill {holder.Name}.");
            }
        }

        foreach (string name in names)
        {
            skillsByName[name] = skill;
        }
    }

    private static IEnumerable<string> NamesOf(Skill skill)
    {
        return skill.Aliases.Prepend(skill.Name).Select(Normalize).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal);
    }
}
