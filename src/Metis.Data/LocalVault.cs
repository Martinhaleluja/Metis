using System.Security.Cryptography;
using System.Text;

namespace Metis.Data;

/// <summary>
/// Encrypts what Metis keeps on disk, tied to the Windows account that wrote it.
///
/// Metis's records are a transcript of everything it has been shown: the
/// questions asked, the answers given, the titles of the windows they were
/// about. They were plain JSON in a well-known folder, which meant a stolen
/// laptop, a resold drive, a shared machine or any other program running as the
/// same user could read the lot.
///
/// DPAPI is the right size of answer here. It is part of Windows, costs
/// nothing, and needs no key for Metis to store — which matters, because a key
/// Metis stored would sit in the same folder as the thing it protects. The
/// trade is that the data is readable only by this Windows account on this
/// machine: copying the file elsewhere makes it useless, and so does resetting
/// the account password by an administrator rather than changing it.
///
/// Reading tolerates plaintext on purpose, so a document written by an older
/// version still opens and is re-encrypted the next time it is saved.
/// </summary>
public static class LocalVault
{
    /// <summary>
    /// Marks a file as one this class wrote. Without it, telling ciphertext from
    /// an old plaintext document would mean guessing.
    /// </summary>
    private static readonly byte[] Marker = "MetisV1"u8.ToArray();

    /// <summary>
    /// Ties the ciphertext to Metis, so a blob lifted from these files cannot be
    /// decrypted by another application running as the same user without also
    /// knowing this.
    /// </summary>
    private static readonly byte[] Entropy = "Metis local record"u8.ToArray();

    /// <summary>
    /// Encrypts text for storage. Returns the bytes to write.
    /// </summary>
    public static byte[] Protect(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText),
            Entropy,
            DataProtectionScope.CurrentUser);

        var stored = new byte[Marker.Length + cipher.Length];
        Marker.CopyTo(stored, 0);
        cipher.CopyTo(stored, Marker.Length);
        return stored;
    }

    /// <summary>
    /// Reads a stored file back.
    ///
    /// A document without the marker is one written before Metis encrypted
    /// anything, and is returned as it is — the alternative is a user losing
    /// their history on upgrade, which is a worse outcome than a file that stays
    /// plain until its next save.
    ///
    /// A document that has the marker but will not decrypt is one written by a
    /// different Windows account, or on a different machine. There is nothing to
    /// recover, so null says so rather than throwing: a chat that cannot be read
    /// should not stop Metis from starting.
    /// </summary>
    public static string? Unprotect(byte[] stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!HasMarker(stored))
        {
            return Encoding.UTF8.GetString(stored);
        }

        try
        {
            var cipher = stored.AsSpan(Marker.Length).ToArray();
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Whether this file was written encrypted.</summary>
    public static bool IsProtected(byte[] stored) => HasMarker(stored);

    private static bool HasMarker(byte[] stored) =>
        stored.Length > Marker.Length && stored.AsSpan(0, Marker.Length).SequenceEqual(Marker);
}
