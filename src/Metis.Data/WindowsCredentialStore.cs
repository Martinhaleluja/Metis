using System.ComponentModel;
using System.Runtime.InteropServices;
using Metis.Core.Contracts;

namespace Metis.Data;

public sealed class WindowsCredentialStore : ISecretStore
{
    private const string GeminiCredentialTarget = "Metis.Gemini.ApiKey";
    private const string OpenAiCredentialTarget = "Metis.OpenAI.ApiKey";
    private const string ClaudeCredentialTarget = "Metis.Claude.ApiKey";
    private const string OpenClawCredentialTarget = "Metis.OpenClaw.Token";
    private const string AssemblyAiCredentialTarget = "Metis.AssemblyAI.ApiKey";
    private const string ElevenLabsCredentialTarget = "Metis.ElevenLabs.ApiKey";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? ReadGeminiApiKey() => ReadSecret(GeminiCredentialTarget, "Gemini");

    public void WriteGeminiApiKey(string apiKey) => WriteSecret(GeminiCredentialTarget, "Gemini", apiKey);

    public void DeleteGeminiApiKey() => DeleteSecret(GeminiCredentialTarget, "Gemini");

    public string? ReadOpenAiApiKey() => ReadSecret(OpenAiCredentialTarget, "OpenAI");

    public void WriteOpenAiApiKey(string apiKey) => WriteSecret(OpenAiCredentialTarget, "OpenAI", apiKey);

    public void DeleteOpenAiApiKey() => DeleteSecret(OpenAiCredentialTarget, "OpenAI");

    public string? ReadClaudeApiKey() => ReadSecret(ClaudeCredentialTarget, "Claude");

    public void WriteClaudeApiKey(string apiKey) => WriteSecret(ClaudeCredentialTarget, "Claude", apiKey);

    public void DeleteClaudeApiKey() => DeleteSecret(ClaudeCredentialTarget, "Claude");

    public string? ReadOpenClawToken() => ReadSecret(OpenClawCredentialTarget, "OpenClaw");

    public void WriteOpenClawToken(string token) => WriteSecret(OpenClawCredentialTarget, "OpenClaw", token);

    public void DeleteOpenClawToken() => DeleteSecret(OpenClawCredentialTarget, "OpenClaw");

    public string? ReadAssemblyAiApiKey() => ReadSecret(AssemblyAiCredentialTarget, "AssemblyAI");

    public void WriteAssemblyAiApiKey(string apiKey) => WriteSecret(AssemblyAiCredentialTarget, "AssemblyAI", apiKey);

    public void DeleteAssemblyAiApiKey() => DeleteSecret(AssemblyAiCredentialTarget, "AssemblyAI");

    public string? ReadElevenLabsApiKey() => ReadSecret(ElevenLabsCredentialTarget, "ElevenLabs");

    public void WriteElevenLabsApiKey(string apiKey) => WriteSecret(ElevenLabsCredentialTarget, "ElevenLabs", apiKey);

    public void DeleteElevenLabsApiKey() => DeleteSecret(ElevenLabsCredentialTarget, "ElevenLabs");

    private static string? ReadSecret(string target, string provider)
    {
        EnsureWindows();
        if (!CredRead(target, CredTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, $"Windows Credential Manager could not read Metis's {provider} key.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == nint.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            return Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    private static void WriteSecret(string target, string provider, string apiKey)
    {
        EnsureWindows();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException($"The {provider} API key cannot be empty.", nameof(apiKey));
        }

        var normalized = apiKey.Trim();
        var blobSize = checked(normalized.Length * sizeof(char));
        var blob = Marshal.StringToCoTaskMemUni(normalized);
        try
        {
            var credential = new NativeCredential
            {
                Flags = 0,
                Type = CredTypeGeneric,
                TargetName = target,
                Comment = null,
                LastWritten = default,
                CredentialBlobSize = checked((uint)blobSize),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Attributes = nint.Zero,
                TargetAlias = null,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, $"Windows Credential Manager could not save Metis's {provider} key.");
            }
        }
        finally
        {
            if (blob != nint.Zero)
            {
                for (var offset = 0; offset < blobSize; offset += sizeof(long))
                {
                    var remaining = blobSize - offset;
                    if (remaining >= sizeof(long))
                    {
                        Marshal.WriteInt64(blob, offset, 0);
                    }
                    else
                    {
                        for (var index = 0; index < remaining; index++)
                        {
                            Marshal.WriteByte(blob, offset + index, 0);
                        }
                    }
                }

                Marshal.FreeCoTaskMem(blob);
            }
        }
    }

    private static void DeleteSecret(string target, string provider)
    {
        EnsureWindows();
        if (CredDelete(target, CredTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error, $"Windows Credential Manager could not remove Metis's {provider} key.");
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Metis's credential store requires Windows.");
        }
    }

#pragma warning disable CS0649 // Native Credential Manager populates these fields when reading.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }
#pragma warning restore CS0649

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
