namespace Metis.Core.Contracts;

/// <summary>
/// Where the last signed entitlement snapshot from the gateway is kept.
///
/// It is its own interface rather than three more members on
/// <c>ISecretStore</c>, which is implemented by the Windows credential store,
/// the local vault, and several test doubles — none of which have any business
/// growing a method about plans. Two small interfaces beat one that has to be
/// implemented three times over for the benefit of one caller.
///
/// The reason it is not simply a field in settings.json is the whole point:
/// settings.json is a plain file the user can edit, and a plan someone can give
/// themselves by editing a file is not a plan. This goes into Windows
/// Credential Manager, and the snapshot inside it is signed, so tampering with
/// either the file or the value fails verification and the client falls back to
/// the free plan rather than to the generous one.
/// </summary>
public interface IEntitlementCache
{
    /// <summary>The last signed snapshot, or null when there is none.</summary>
    string? Read();

    void Write(string signedSnapshot);

    /// <summary>Called on sign-out. A cached plan must not outlive its account.</summary>
    void Clear();
}
