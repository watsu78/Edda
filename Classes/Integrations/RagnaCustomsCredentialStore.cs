using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Edda.Classes.Integrations {
    // Keep the API key out of settings.txt and map exports. Windows protects it
    // for the current user; no RagnaCustoms password is stored by Edda.
    public class RagnaCustomsCredentialStore {
        private const string Target = "Edda:RagnaCustoms:ApiKey";
        private const int CredentialTypeGeneric = 1;
        private const int PersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;
        private const int MaxCredentialBytes = 2560;
        private readonly string targetName;

        public RagnaCustomsCredentialStore() : this(Target) { }

        internal RagnaCustomsCredentialStore(string targetName) {
            this.targetName = targetName;
        }

        public string Read() {
            if (!CredRead(targetName, CredentialTypeGeneric, 0, out IntPtr pointer)) {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) {
                    return null;
                }
                throw new Win32Exception(error);
            }
            try {
                Credential credential = Marshal.PtrToStructure<Credential>(pointer);
                return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            } finally {
                CredFree(pointer);
            }
        }

        public void Save(string apiKey) {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey != apiKey.Trim() || apiKey.Contains('\r') || apiKey.Contains('\n')) {
                throw new ArgumentException("Enter an API key without spaces or line breaks at either end.", nameof(apiKey));
            }
            int byteCount = Encoding.Unicode.GetByteCount(apiKey);
            if (byteCount > MaxCredentialBytes) {
                throw new ArgumentException("The API key is too long for Windows Credential Manager.", nameof(apiKey));
            }
            IntPtr secret = Marshal.StringToCoTaskMemUni(apiKey);
            try {
                Credential credential = new() {
                    Type = CredentialTypeGeneric,
                    TargetName = targetName,
                    CredentialBlobSize = (uint)byteCount,
                    CredentialBlob = secret,
                    Persist = PersistLocalMachine,
                    UserName = "RagnaCustoms"
                };
                if (!CredWrite(ref credential, 0)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            } finally {
                Marshal.ZeroFreeCoTaskMemUnicode(secret);
            }
        }

        public void Delete() {
            if (!CredDelete(targetName, CredentialTypeGeneric, 0)) {
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorNotFound) {
                    throw new Win32Exception(error);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);
    }
}