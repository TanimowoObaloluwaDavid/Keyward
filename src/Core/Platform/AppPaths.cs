using System;
using System.IO;
using System.Security.AccessControl;
using Keyward.Core.Storage;

namespace Keyward.Core.Services
{
    /// <summary>Where Keyward keeps its files.</summary>
    /// <remarks>
    /// Everything lives under a single folder in the user's roaming profile so the product is
    /// trivially portable and there is nowhere surprising for data to hide. The folder is created
    /// with an owner-only ACL the first time it is made.
    /// </remarks>
    public static class AppPaths
    {
        private const string FolderName = "Keyward";

        /// <summary>Application data folder, e.g. C:\Users\&lt;you&gt;\AppData\Roaming\Keyward.</summary>
        public static string DataDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, FolderName);
            }
        }

        /// <summary>Default vault file.</summary>
        public static string DefaultVaultPath
        {
            get { return Path.Combine(DataDirectory, "vault.kvw"); }
        }

        /// <summary>Non-secret preferences.</summary>
        public static string SettingsPath
        {
            get { return Path.Combine(DataDirectory, "settings.json"); }
        }

        /// <summary>Where encrypted backups are written.</summary>
        public static string BackupDirectory
        {
            get { return Path.Combine(DataDirectory, "backups"); }
        }

        /// <summary>Creates the data folder and locks its permissions if it does not exist.</summary>
        public static void EnsureDataDirectory()
        {
            string dir = DataDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                RestrictDirectory(dir);
            }
        }

        /// <summary>Applies an owner-only ACL to a directory.</summary>
        public static void RestrictDirectory(string path)
        {
            try
            {
                DirectorySecurity security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);

                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();

                if (identity.User != null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        identity.User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                }

                security.AddAccessRule(new FileSystemAccessRule(
                    new System.Security.Principal.SecurityIdentifier(
                        System.Security.Principal.WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, InheritanceFlags.ContainerInherit,
                    PropagationFlags.None, AccessControlType.Allow));

                Directory.SetAccessControl(path, security);
            }
            catch (Exception)
            {
                // Permission tightening is defence in depth on top of encryption at rest.
                // Failing to apply it must never stop the user from using their vault.
            }
        }
    }
}
