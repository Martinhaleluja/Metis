namespace Metis.Core.Models;

/// <summary>
/// Which deployment a copy of Metis is talking to.
///
/// Kept as a value the client reads rather than something it decides, so a
/// development build cannot end up writing to production data because a URL was
/// left edited in a settings file. Unrecognised values resolve to
/// <see cref="Production"/>: it is the most restricted environment, and guessing
/// wrong towards more access is the only failure that costs anything.
/// </summary>
public enum MetisEnvironment
{
    Development,
    Staging,
    Production
}

/// <summary>
/// What a signed-in person is, as far as permission is concerned.
///
/// Roles come from the backend and are never taken from anything the client
/// says about itself. A desktop application can be edited by whoever runs it,
/// so a role it asserts is a request, not a fact.
/// </summary>
public enum UserRole
{
    User,
    Pro,
    Developer,
    Founder,
    Admin
}

/// <summary>
/// The billing state, separate from the role. A founder is not a paying
/// customer and a Pro subscriber is not staff, and conflating the two is what
/// leads to giving away paid features to test accounts.
///
/// The declaration order is load-bearing: <see cref="MetisAccount.IsAtLeast"/>
/// compares these ordinally, so a tier inserted out of order would quietly make
/// a smaller plan test as larger than a bigger one. Inserting Plus in the middle
/// is safe because nothing persists this numerically — it is stored by name in a
/// Postgres enum, parsed from a string by <c>Entitlements.ParsePlan</c>, and
/// absent from settings.json entirely.
/// </summary>
public enum PlanTier
{
    Free,
    Plus,
    Pro
}

/// <summary>
/// A named capability, rather than a role check written out at the call site.
///
/// The specification is explicit that checks like "if the email is mine" must
/// not be scattered through the application, and the reason is not tidiness: a
/// check written twice will eventually be written differently, and the copy
/// that is wrong is the one that grants access it should not. Adding a plan
/// later should mean editing one table, not auditing every call site.
/// </summary>
public enum MetisFeature
{
    /// <summary>Connecting a personal provider API key. Pro and staff only.</summary>
    CustomAiProvider,

    /// <summary>
    /// Operating the computer directly. Metis is a learning instrument and does
    /// not do this any more; the value survives because the server-side enum it
    /// mirrors still carries it, and the two have to agree.
    /// </summary>
    ComputerControl,

    /// <summary>Running system commands for things with no interface.</summary>
    SystemCommands,

    /// <summary>The diagnostics panel. Never available to ordinary accounts.</summary>
    DeveloperMode,

    /// <summary>Features still behind a flag.</summary>
    ExperimentalFeatures,

    /// <summary>Pointing a build at staging.</summary>
    StagingAccess,

    /// <summary>The internal metrics dashboard.</summary>
    AdminDashboard,

    /// <summary>Seeing what a request cost.</summary>
    InternalCostVisibility,

    // ---- What Metis pays for ------------------------------------------------
    //
    // Everything below gates spending rather than behaviour. That distinction is
    // the whole reason these are named the way they are, and it is worth stating
    // once here rather than defending in ten places:
    //
    // A person running Metis on their own API key is not asking Metis to buy
    // them anything, so none of these apply to them. They keep vision, keep
    // automation, keep agents — on Free, signed out, forever — because the cost
    // of all of it lands on their own provider account and not on Metis's. The
    // rule lives in ProviderRouting.Decide, which never reaches the gateway for
    // a bring-your-own-key turn, so these checks are never even consulted.
    //
    // Getting this backwards would take working features away from the people
    // who have been running Metis since before there was anything to buy, which
    // the comment on Entitlements.BillingIsLive promised would not happen.

    /// <summary>May send anything through the gateway on Metis's own key at all.</summary>
    ManagedAiRouting,

    /// <summary>Managed access to providers beyond Gemini. Plus and above.</summary>
    ManagedPremiumModels,

    /// <summary>
    /// May attach a screenshot to a <em>managed</em> request. An image is by far
    /// the most expensive part of a turn, which makes this the primary cost
    /// lever between Free and Plus.
    /// </summary>
    ManagedScreenVision,

    /// <summary>Accessibility-element context, region inspect, pointer inspect.</summary>
    AdvancedAutomation,

    /// <summary>The background agent runner.</summary>
    AutonomousAgents,

    /// <summary>Multi-agent spawn and long-horizon runs. Pro and above.</summary>
    AdvancedAgents,

    /// <summary>Memory beyond the Free cap. The size itself is a plan limit.</summary>
    PersistentMemory,

    /// <summary>Browser-context assistance.</summary>
    BrowserAssistance,

    /// <summary>Seeing your own allowance and what you have used of it.</summary>
    UsageVisibility,

    /// <summary>Full provider, model and endpoint control. Pro and above.</summary>
    ProviderManagement
}

/// <summary>
/// The signed-in account as the client understands it.
///
/// Everything here arrives from the backend. The client uses it to decide what
/// to show, and never to decide what to permit — the same entitlement is
/// checked again server-side before anything is actually done, because a value
/// that travelled through a program the user controls cannot be evidence.
/// </summary>
public sealed record MetisAccount(
    string UserId,
    UserRole Role,
    PlanTier Plan,
    MetisEnvironment Environment,
    bool EmailVerified = true)
{
    /// <summary>
    /// Nobody signed in. Signed-out Metis still runs, so this has to be a real
    /// value rather than a null that every caller has to remember to handle.
    /// </summary>
    public static MetisAccount SignedOut { get; } =
        new(string.Empty, UserRole.User, PlanTier.Free, MetisEnvironment.Production, EmailVerified: false);

    public bool IsSignedIn => UserId.Length > 0;

    /// <summary>True for the roles that belong to whoever builds Metis.</summary>
    public bool IsStaff => Role is UserRole.Developer or UserRole.Founder or UserRole.Admin;

    /// <summary>
    /// Whether this account is on <paramref name="tier"/> or a larger one. Reads
    /// better than a chain of or-patterns at every call site, and depends on the
    /// declaration order of <see cref="PlanTier"/> being smallest-first.
    /// </summary>
    public bool IsAtLeast(PlanTier tier) => Plan >= tier;
}

/// <summary>
/// What the server says an account may do, at a moment in time.
///
/// This exists because the client and the server have to agree about billing
/// without the client being able to decide the answer. The gateway computes it
/// from the database and signs it; the desktop shows what it says and caches it
/// so a paying user who is offline for a week is not silently treated as Free.
///
/// It is evidence for what to <em>display</em>, and nothing more. Every request
/// is checked again server-side, because a snapshot that has been through a
/// program the user controls is a claim rather than a fact.
/// </summary>
public sealed record EntitlementSnapshot(
    string UserId,
    UserRole Role,
    PlanTier Plan,
    bool EmailVerified,
    bool BillingIsLive,
    IReadOnlySet<MetisFeature> Granted,
    PlanLimits Limits,
    DateTimeOffset IssuedUtc,
    DateTimeOffset ExpiresUtc)
{
    public bool Has(MetisFeature feature) => Granted.Contains(feature);

    public bool IsUsableAt(DateTimeOffset now, string forUserId) =>
        now < ExpiresUtc && string.Equals(UserId, forUserId, StringComparison.Ordinal);
}

/// <summary>
/// The numbers behind a plan, kept out of the code on purpose.
///
/// These are business assumptions, and business assumptions measured against
/// real usage change faster than releases ship. They live in a database table
/// the gateway reads, travel to the client inside an
/// <see cref="EntitlementSnapshot"/>, and are never compiled in — so raising an
/// allowance is a row update rather than a new build for everyone.
/// </summary>
public sealed record PlanLimits(
    decimal MonthlyBudgetUsd,
    int MaxScreenshotBytes,
    int RequestsPerMinute,
    int BurstRequests,
    int MaxAgentStepsPerMonth,
    int MaxAgentStepsPerTask,
    int MemoryEntriesMax,
    IReadOnlyList<string> ManagedModels)
{
    /// <summary>
    /// What to assume when the server has not been reached yet. Deliberately
    /// small: an allowance guessed too high shows someone a budget they do not
    /// have and lets the client send a screenshot the gateway will only refuse.
    /// </summary>
    public static PlanLimits Unknown { get; } =
        new(0m, 0, 3, 3, 0, 0, 0, Array.Empty<string>());
}

/// <summary>
/// A turn refused because of the account rather than the request: the plan does
/// not cover it, or the month's included AI is spent.
///
/// Its own type rather than a pair of strings so the banner cannot be shown with
/// the two the wrong way round, and so a third field can be added later without
/// changing every call site.
/// </summary>
public sealed record PlanLimitNotice(string Title, string Detail);
