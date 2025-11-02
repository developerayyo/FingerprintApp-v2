using System;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace ERPNextFingerprintApp.Services
{
    public class CredentialStorageService
    {
        private const string TargetName = "ERPNextFingerprintApp_Credentials";
        
        public bool SaveCredentials(string username, string password)
        {
            try
            {
                var credential = new CREDENTIAL
                {
                    TargetName = TargetName,
                    UserName = username,
                    Type = CRED_TYPE.GENERIC,
                    Persist = CRED_PERSIST.LOCAL_MACHINE
                };
                
                credential.CredentialBlobBytes = Encoding.UTF8.GetBytes(password);

                bool result = CredWrite(ref credential, 0);
                if (result)
                {
                    Log.Information("Credentials saved successfully for user: {Username}", username);
                }
                else
                {
                    Log.Warning("Failed to save credentials for user: {Username}", username);
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving credentials for user: {Username}", username);
                return false;
            }
        }

        public (string Username, string Password)? GetCredentials()
        {
            try
            {
                bool result = CredRead(TargetName, CRED_TYPE.GENERIC, 0, out IntPtr credPtr);
                if (!result)
                {
                    return null;
                }

                var credential = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                string username = credential.UserName;
                byte[] passwordBytes = credential.CredentialBlobBytes;
                string password = Encoding.UTF8.GetString(passwordBytes);

                CredFree(credPtr);

                Log.Information("Retrieved saved credentials for user: {Username}", username);
                return (username, password);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving saved credentials");
                return null;
            }
        }

        public (string username, string password)? GetSavedCredentials()
        {
            return GetCredentials();
        }

        public bool ClearCredentials()
        {
            try
            {
                bool result = CredDelete(TargetName, CRED_TYPE.GENERIC, 0);
                if (result)
                {
                    Log.Information("Saved credentials deleted successfully");
                }
                else
                {
                    Log.Warning("Failed to delete saved credentials or no credentials found");
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deleting saved credentials");
                return false;
            }
        }

        public bool DeleteSavedCredentials()
        {
            return ClearCredentials();
        }

        public bool HasSavedCredentials()
        {
            try
            {
                bool result = CredRead(TargetName, CRED_TYPE.GENERIC, 0, out IntPtr credPtr);
                if (result)
                {
                    CredFree(credPtr);
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking for saved credentials");
                return false;
            }
        }

        #region Windows Credential Manager API

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CredDelete(string target, CRED_TYPE type, int flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree([In] IntPtr cred);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public CRED_TYPE Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public CRED_PERSIST Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;

            public byte[] CredentialBlobBytes
            {
                get
                {
                    if (CredentialBlob == IntPtr.Zero || CredentialBlobSize == 0)
                        return new byte[0];

                    byte[] bytes = new byte[CredentialBlobSize];
                    Marshal.Copy(CredentialBlob, bytes, 0, (int)CredentialBlobSize);
                    return bytes;
                }
                set
                {
                    if (value == null)
                    {
                        CredentialBlob = IntPtr.Zero;
                        CredentialBlobSize = 0;
                    }
                    else
                    {
                        CredentialBlob = Marshal.AllocHGlobal(value.Length);
                        Marshal.Copy(value, 0, CredentialBlob, value.Length);
                        CredentialBlobSize = (uint)value.Length;
                    }
                }
            }
        }

        private enum CRED_TYPE : uint
        {
            GENERIC = 1,
            DOMAIN_PASSWORD = 2,
            DOMAIN_CERTIFICATE = 3,
            DOMAIN_VISIBLE_PASSWORD = 4,
            GENERIC_CERTIFICATE = 5,
            DOMAIN_EXTENDED = 6,
            MAXIMUM = 7,
            MAXIMUM_EX = (MAXIMUM + 1000),
        }

        private enum CRED_PERSIST : uint
        {
            SESSION = 1,
            LOCAL_MACHINE = 2,
            ENTERPRISE = 3,
        }

        #endregion
    }
}