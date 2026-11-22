using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SplatWin.Core;

/// <summary>
/// A user name and password kept in Windows Credential Manager (a "generic" credential). Windows
/// encrypts it for the signed-in Windows user; it is never written to a file by this app.
/// It shows in Control Panel → Credential Manager → Windows Credentials under its target name.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WinCredentials
{
    public const string QrzTarget = "SPLATforWindows/QRZ.com";

    public static void Save(string target, string user, string password)
    {
        byte[] blob = Encoding.Unicode.GetBytes(password);
        if (blob.Length > 2560) throw new ArgumentException("The password is too long to store.");
        IntPtr p = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, p, blob.Length);
            var c = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC, TargetName = target, UserName = user,
                CredentialBlobSize = (uint)blob.Length, CredentialBlob = p,
                Persist = CRED_PERSIST_LOCAL_MACHINE,   // this Windows user, on this PC (doesn't roam)
                Comment = "QRZ.com sign-in for SPLAT! for Windows",
            };
            if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            for (int i = 0; i < blob.Length; i++) Marshal.WriteByte(p, i, 0);
            Marshal.FreeHGlobal(p);
            Array.Clear(blob);
        }
    }

    /// <summary>The stored user name and password, or null if there is none.</summary>
    public static (string User, string Password)? Read(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr p))
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_FOUND) return null;
            throw new Win32Exception(err);
        }
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(p);
            string pw = c.CredentialBlobSize == 0 ? "" : Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2);
            return (c.UserName ?? "", pw);
        }
        finally { CredFree(p); }
    }

    /// <summary>Removes it. Returns false if there was none.</summary>
    public static bool Delete(string target)
    {
        if (CredDelete(target, CRED_TYPE_GENERIC, 0)) return true;
        int err = Marshal.GetLastWin32Error();
        if (err == ERROR_NOT_FOUND) return false;
        throw new Win32Exception(err);
    }

    const uint CRED_TYPE_GENERIC = 1;
    const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr buffer);
}
