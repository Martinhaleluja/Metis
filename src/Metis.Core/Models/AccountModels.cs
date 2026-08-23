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
/// </summary>
public enum PlanTier
{
    Free,
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
    InternalCostVisibility
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
}
