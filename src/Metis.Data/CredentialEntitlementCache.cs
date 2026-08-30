using Metis.Core.Contracts;

namespace Metis.Data;

/// <summary>
/// Keeps the last signed entitlement snapshot in Windows Credential Manager.
///
/// Not because the snapshot is secret — it says which plan someone bought, which
/// they already know — but because Credential Manager is the one store on this
/// machine that is not a text file the user is invited to edit. Combined with
/// the signature the gateway puts on it, that makes "give myself Pro while
/// offline" require patching the binary rather than opening Notepad.
///
/// It reuses <see cref="WindowsCredentialStore"/>'s Win32 plumbing through
/// composition rather than inheriting or extending <c>ISecretStore</c>, so
/// nothing that only needs API keys ends up carrying methods about plans.
/// </summary>
public sealed class CredentialEntitlementCache : IEntitlementCache
{
    private const string Target = "Metis/Entitlements/Snapshot";

    private readonly WindowsCredentialStore _store = new();

    public string? Read()
    {
        try
        {
            return _store.ReadRaw(Target, "entitlements");
        }
        catch (Exception)
        {
            // A cache that cannot be read is the same as an empty one: ask the
            // server. Failing the whole start-up because Credential Manager was
            // briefly unhappy would be a far worse outcome than one extra
            // request.
            return null;
        }
    }

    public void Write(string signedSnapshot)
    {
        try
        {
            _store.WriteRaw(Target, "entitlements", signedSnapshot);
        }
        catch (Exception)
        {
            // Losing the cache costs an offline user their plan display for one
            // session. Losing the turn they were in the middle of would be
            // worse, so this never throws.
        }
    }

    public void Clear()
    {
        try
        {
            _store.DeleteRaw(Target, "entitlements");
        }
        catch (Exception)
        {
            // Nothing useful to do. The snapshot carries the user id it was
            // issued for and is rejected for anyone else, so a stale one that
            // survives a failed sign-out still cannot be used by the next
            // person to sign in.
        }
    }
}
