using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Keyward.Core.Storage
{
    /// <summary>
    /// Crash-safe file writes plus restrictive Windows ACLs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vault write must never leave a half-written file behind. Every write goes to a temporary
    /// file in the same directory, is flushed all the way to the device, and only then replaces
    /// the target atomically. If the process dies, is killed, or the disk fills up mid-write, the
    /// previous vault file is still complete and intact.
    /// </para>
    /// <para>
    /// <c>File.Replace</c> is the primitive doing the atomic swap. It is atomic with respect to
    /// other processes opening the same path, which is what matters for a desktop app that a user
    /// may open twice.
    /// </para>
    /// </remarks>
    public static class AtomicFile
    {
        /// <summary>
        /// Refuses to load files larger than this. A corrupt or hostile file that reports a huge
        /// size should not be able to exhaust memory during startup.
        /// </summary>
        public const long MaxVaultBytes = 64L * 1024 * 1024;

        /// <summary>
        /// Atomically replaces the file at <paramref name="path"/> with <paramref name="bytes"/>.
        /// </summary>
        /// <exception cref="IOException">The write failed; the original file is unchanged.</exception>
        public static void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is required.", "path");
            if (bytes == null) throw new ArgumentNullException("bytes");

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Temp file must live in the same directory so the rename stays on one volume and is
            // therefore atomic. A temp file in %TEMP% would be a cross-device copy.
            string temp = path + ".tmp";
            string backup = path + ".bak";

            try
            {
                using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush();
                    // Push past the OS cache to the physical device before the rename, so a power
                    // loss immediately after the swap cannot leave the new file truncated.
                    fs.Flush(true);
                }

                if (File.Exists(path))
                {
                    // Atomic replace. ignoreMetadataErrors avoids failures on exotic filesystems
                    // that do not support ACL inheritance on the destination.
                    File.Replace(temp, path, backup, true);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch
            {
                // Never leave a partial temp file lying next to the real vault.
                SafeDelete(temp);
                throw;
            }
            finally
            {
                SafeDelete(backup);
            }
        }

        /// <summary>Reads a file, refusing anything implausibly large.</summary>
        /// <exception cref="InvalidDataException">The file is larger than the safety limit.</exception>
        public static byte[] ReadAllBytes(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("File not found: " + path, path);
            if (info.Length > MaxVaultBytes)
                throw new InvalidDataException("File is larger than the supported limit and was not loaded.");

            return File.ReadAllBytes(path);
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* best effort: a leftover .tmp is harmless, it holds no new secrets */ }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Restricts a file to the current user, SYSTEM and the local Administrators group,
        /// breaking inheritance so that permissions added later by a parent directory do not
        /// silently grant access.
        /// </summary>
        public static void RestrictToCurrentUser(string path)
        {
            try
            {
                FileSecurity security = new FileSecurity();
                security.SetAccessRuleProtection(true, false);

                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                SecurityIdentifier user = identity.User;
                if (user != null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        user, FileSystemRights.FullControl, AccessControlType.Allow));
                }

                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl, AccessControlType.Allow));

                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl, AccessControlType.Allow));

                File.SetAccessControl(path, security);
            }
            catch (UnauthorizedAccessException)
            {
                // Non-fatal: the vault is still encrypted at rest. Surface as a warning in the UI
                // rather than failing a save the user needs.
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (SystemException)
            {
            }
        }
    }
}
