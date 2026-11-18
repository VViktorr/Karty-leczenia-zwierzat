using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KartyLeczenia.Windows;

/// <summary>
/// Stores SMB authentication in the current user's Windows Credential Manager.
/// No passwords are saved in KartyLeczenia application configuration.
/// </summary>
internal static class UpdateCredentials
{
    const uint CredentialDomainPassword = 2;
    const uint PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    static extern void CredFree(IntPtr credential);

    internal static string ServerFromShare(string share)
    {
        if (string.IsNullOrWhiteSpace(share) || !share.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Adres aktualizacji musi mieć postać \\\\SERWER\\UDZIAL.");
        string[] parts = share.TrimStart('\\').Split('\\');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new ArgumentException("Nieprawidłowy adres udziału aktualizacji.");
        return parts[0];
    }

    internal static string? SavedUser(string server)
    {
        if (!CredRead(server, CredentialDomainPassword, 0, out var pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 1168) return null; // ERROR_NOT_FOUND
            throw new Win32Exception(error, "Nie można odczytać poświadczeń Windows.");
        }
        try { return Marshal.PtrToStructure<NativeCredential>(pointer).UserName; }
        finally { CredFree(pointer); }
    }

    internal static void Save(string server, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(server) || server.Contains('\\') || server.Contains('/'))
            throw new ArgumentException("Nieprawidłowa nazwa serwera aktualizacji.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Wpisz nazwę użytkownika i hasło.");
        if (password.Length * sizeof(char) > 2560)
            throw new ArgumentException("Hasło jest za długie.");

        IntPtr blob = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredentialDomainPassword,
                TargetName = server,
                UserName = username.Trim(),
                CredentialBlob = blob,
                CredentialBlobSize = (uint)(password.Length * sizeof(char)),
                Persist = PersistLocalMachine,
                Comment = "KartyLeczenia - aktualizacje z udziału sieciowego"
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nie można zapisać poświadczeń Windows.");
        }
        finally
        {
            // Wipe the unmanaged UTF-16 buffer before freeing it.
            for (int i = 0; i < password.Length * sizeof(char); i++)
                Marshal.WriteByte(blob, i, 0);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    internal static void Delete(string server)
    {
        if (CredDelete(server, CredentialDomainPassword, 0)) return;
        int error = Marshal.GetLastWin32Error();
        if (error != 1168) throw new Win32Exception(error, "Nie można usunąć poświadczeń.");
    }

    internal static string Test(string share)
    {
        string path = Path.Combine(share, "release.json");
        string manifest = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(manifest))
            throw new IOException("Manifest release.json jest pusty.");
        return manifest;
    }
}
