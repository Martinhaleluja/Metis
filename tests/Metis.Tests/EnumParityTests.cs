using System.Text.RegularExpressions;
using Metis.Core.Models;

namespace Metis.Tests;

/// <summary>
/// That the C# enums and the Postgres enums still say the same thing.
///
/// Every comment in Entitlements.cs and AccountModels.cs warns about the same
/// hazard: a permission rule written in two places will eventually be written
/// differently, and the copy that drifts is the one that grants access it should
/// not. The plan and feature enums are that rule's vocabulary, and they live in
/// two languages — C# for the client and the gateway, Postgres for row level
/// security. A capability that exists on one side and not the other is a
/// question with two different answers.
///
/// This reads the migration files from disk rather than the live database on
/// purpose: the migrations are what any environment gets rebuilt from, so they
/// are the thing that has to be right.
///
/// The pattern follows ThemeTokenParityTests, which already does exactly this
/// for the light and dark theme dictionaries.
/// </summary>
public sealed class EnumParityTests
{
    /// <summary>
    /// Enum values Postgres still carries that C# has dropped.
    ///
    /// Postgres cannot remove a value from an enum without rewriting every
    /// dependent column, so a renamed plan leaves its old name behind for good.
    /// "plus" is the former name of the middle plan, now called Pro; nothing
    /// writes it any more and Entitlements.ParsePlan maps it to Pro so an old
    /// row does not silently demote its owner to Free.
    ///
    /// This is an allow-list rather than a loosened comparison on purpose. A
    /// value that appears in Postgres and not in C# is normally exactly the
    /// drift this test exists to catch, and every exception to that should have
    /// to be written down here with a reason.
    /// </summary>
    private static readonly string[] RetiredPlanTiers = ["plus"];

    [Fact]
    public void The_plan_tiers_match()
    {
        var declared = ReadEnumValues("plan_tier")
            .Except(RetiredPlanTiers, StringComparer.Ordinal);

        Assert.Equal(
            Enum.GetValues<PlanTier>().Select(Snake).OrderBy(name => name, StringComparer.Ordinal),
            declared.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// A retired value has to actually be retired. If someone re-adds "plus" to
    /// the C# enum, the allow-list above would quietly hide the mismatch rather
    /// than report it.
    /// </summary>
    [Fact]
    public void Retired_tiers_are_gone_from_the_client()
    {
        var live = Enum.GetValues<PlanTier>().Select(Snake).ToHashSet(StringComparer.Ordinal);

        foreach (var retired in RetiredPlanTiers)
        {
            Assert.DoesNotContain(retired, live);
        }
    }

    [Fact]
    public void The_features_match()
    {
        var declared = ReadEnumValues("metis_feature");
        var compiled = Enum.GetValues<MetisFeature>().Select(Snake).ToHashSet(StringComparer.Ordinal);

        var missingInSql = compiled.Except(declared, StringComparer.Ordinal).ToArray();
        var missingInCode = declared.Except(compiled, StringComparer.Ordinal).ToArray();

        Assert.True(
            missingInSql.Length == 0,
            "These capabilities exist in C# but not in the Postgres metis_feature enum, so row level "
            + "security cannot express them: " + string.Join(", ", missingInSql));

        Assert.True(
            missingInCode.Length == 0,
            "These capabilities exist in Postgres but not in C#, so the client cannot ask about them: "
            + string.Join(", ", missingInCode));
    }

    [Fact]
    public void The_roles_match()
    {
        var declared = ReadEnumValues("user_role");

        Assert.Equal(
            Enum.GetValues<UserRole>().Select(Snake).OrderBy(name => name, StringComparer.Ordinal),
            declared.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void The_environments_match()
    {
        var declared = ReadEnumValues("metis_environment");

        Assert.Equal(
            Enum.GetValues<MetisEnvironment>().Select(Snake).OrderBy(name => name, StringComparer.Ordinal),
            declared.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Reads every value of a Postgres enum out of the migration history: the
    /// values it was created with, plus everything a later migration added.
    /// </summary>
    private static IReadOnlyCollection<string> ReadEnumValues(string typeName)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(MigrationsDirectory(), "*.sql").OrderBy(path => path))
        {
            var sql = File.ReadAllText(file);

            foreach (Match creation in Regex.Matches(
                         sql,
                         @"create\s+type\s+(?:public\.)?" + typeName + @"\s+as\s+enum\s*\(([^)]*)\)",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                foreach (Match literal in Regex.Matches(creation.Groups[1].Value, @"'([^']+)'"))
                {
                    values.Add(literal.Groups[1].Value);
                }
            }

            foreach (Match added in Regex.Matches(
                         sql,
                         @"alter\s+type\s+(?:public\.)?" + typeName
                         + @"\s+add\s+value\s+(?:if\s+not\s+exists\s+)?'([^']+)'",
                         RegexOptions.IgnoreCase))
            {
                values.Add(added.Groups[1].Value);
            }
        }

        Assert.True(values.Count > 0, $"No migration declares the Postgres enum '{typeName}'.");
        return values;
    }

    /// <summary>
    /// Walks up from the test binary to the repository root. The migrations are
    /// not copied to the output directory on purpose: copying them would mean a
    /// stale copy could pass while the real ones had drifted.
    /// </summary>
    private static string MigrationsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "supabase", "migrations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find supabase/migrations above " + AppContext.BaseDirectory);
    }

    /// <summary>PascalCase to snake_case, the convention the migrations use.</summary>
    private static string Snake<T>(T value) where T : struct, Enum =>
        Regex.Replace(value.ToString()!, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}
