using System.ComponentModel;
using System.Runtime.InteropServices;
using OSSStudio.Models;

namespace OSSStudio.Services;

public sealed class BucketCredentialStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public BucketCredential? Load(string bucketId)
    {
        if (!CredRead(GetTarget(bucketId), GenericCredential, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            var secret = credential.CredentialBlob == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? string.Empty;
            return new BucketCredential(credential.UserName ?? string.Empty, secret);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Save(string bucketId, BucketCredential credential)
    {
        var secretPointer = Marshal.StringToCoTaskMemUni(credential.AccessKeySecret);
        try
        {
            var nativeCredential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = GetTarget(bucketId),
                CredentialBlobSize = (uint)(credential.AccessKeySecret.Length * sizeof(char)),
                CredentialBlob = secretPointer,
                Persist = PersistLocalMachine,
                UserName = credential.AccessKeyId
            };

            if (!CredWrite(ref nativeCredential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将 AccessKey 保存到 Windows 凭据管理器。");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(secretPointer);
        }
    }

    public void Delete(string bucketId)
    {
        if (!CredDelete(GetTarget(bucketId), GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "无法从 Windows 凭据管理器删除 AccessKey。");
            }
        }
    }

    private static string GetTarget(string bucketId) => $"OSS-Studio/{bucketId}";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
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

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
