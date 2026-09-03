namespace Metis.Core.Contracts;

/// <summary>
/// The narrow slice of the credential store a sign-in surface needs.
///
/// Passing the whole store would hand a panel that collects a password the
/// ability to read every provider key on the machine, which is a much larger
/// permission than signing in requires.
///
/// This used to be nested inside the account window. It is here now because the
/// account window is gone and the interface outlived it: the in-notch sign-in
/// panel is the live surface, and putting a contract in Metis.Core rather than
/// inside whichever window happened to declare it first means the next surface
/// that needs a session token does not have to reach into a WPF file to find
/// the type.
/// </summary>
public interface ISessionTokenAccess
{
    string? ReadSupabaseRefreshToken();

    void WriteSupabaseRefreshToken(string token);

    void DeleteSupabaseRefreshToken();
}
