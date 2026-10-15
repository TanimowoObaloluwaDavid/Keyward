using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Keyward.Core.Domain;
using Keyward.Core.Services;

namespace Keyward.Tests
{
    /// <summary>
    /// Tests for the unlocked-session behaviour: locking, persistence and the guarantees the UI
    /// relies on.
    /// </summary>
    public static class SessionTests
    {
        public static void Run(string scratchDir)
        {
            Check.Suite("Vault session");
            Creation(scratchDir);
            Persistence(scratchDir);
            Locking(scratchDir);
            MasterPasswordChange(scratchDir);
        }

        private static string Path_(string scratchDir, string name)
        {
            return Path.Combine(scratchDir, name + ".kvw");
        }

        private static void Creation(string scratchDir)
        {
            Check.Case("creating a vault writes the file immediately", delegate
            {
                // Regression: the vault used to live only in memory until the first edit, so a
                // brand-new vault with no entries vanished when the application closed.
                string path = Path_(scratchDir, "session-create");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    UnlockOutcome outcome = session.Unlock("a-good-master-password");
                    Check.True(outcome.Success, "unlock failed: " + outcome.Message);
                    Check.True(outcome.VaultCreated, "expected a newly created vault");
                    Check.True(File.Exists(path),
                        "no vault file was written; a new vault would not survive a restart");
                }

                Check.True(File.Exists(path), "the vault file vanished when the session closed");
            });

            Check.Case("a refused unlock leaves no vault behind", delegate
            {
                string path = Path_(scratchDir, "session-refused");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    UnlockOutcome outcome = session.Unlock("");
                    Check.False(outcome.Success, "an empty master password must be refused");
                }

                Check.False(File.Exists(path), "a refused unlock still created a vault file");
            });
        }

        private static void Persistence(string scratchDir)
        {
            Check.Case("items survive a close and reopen", delegate
            {
                string path = Path_(scratchDir, "session-persist");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    Check.True(session.Unlock("master-password-1").Success, "first unlock failed");

                    Credential c = session.CreateCredential();
                    c.Name = "Persisted login";
                    c.Username = "me@example.com";
                    c.Password = "s3cret-value";
                    session.Commit(c);
                }

                using (VaultSession again = new VaultSession(path))
                {
                    Check.True(again.Unlock("master-password-1").Success, "could not reopen the vault");
                    Check.AreEqual(1, again.Credentials.Count, "the saved item did not come back");
                    Check.AreEqual("s3cret-value", again.Credentials[0].Password, "the password did not survive");
                }
            });

            Check.Case("deleting an item is persisted", delegate
            {
                string path = Path_(scratchDir, "session-delete");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    session.Unlock("master-password-2");
                    Credential keep = session.CreateCredential();
                    keep.Name = "Keep me";
                    session.Commit(keep);

                    Credential drop = session.CreateCredential();
                    drop.Name = "Delete me";
                    session.Commit(drop);
                    session.Delete(drop);

                    Check.AreEqual(1, session.Credentials.Count, "the item was not removed from the document");
                }

                using (VaultSession again = new VaultSession(path))
                {
                    again.Unlock("master-password-2");
                    Check.AreEqual(1, again.Credentials.Count, "the deletion was not persisted");
                }
            });
        }

        private static void Locking(string scratchDir)
        {
            Check.Case("locking destroys the key and hides the contents", delegate
            {
                string path = Path_(scratchDir, "session-lock");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    session.Unlock("master-password-3");
                    session.CreateCredential();

                    Check.True(session.IsUnlocked, "session should be unlocked");
                    session.Lock();

                    Check.False(session.IsUnlocked, "session reported unlocked after locking");

                    // Reading the document while locked must throw, not quietly return nothing -
                    // silently returning an empty list would read as "the user lost their data".
                    Check.Throws<InvalidOperationException>(delegate
                    {
                        List<Credential> ignored = session.Credentials;
                    }, "reading the document while locked must fail loudly");
                }
            });

            Check.Case("auto-lock expires only when armed", delegate
            {
                string path = Path_(scratchDir, "session-idle");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    session.Unlock("master-password-4");

                    session.ArmAutoLock(0);
                    System.Threading.Thread.Sleep(50);
                    Check.False(session.IsIdleExpired, "auto-lock expired while disabled");

                    session.ArmAutoLock(1);
                    Check.False(session.IsIdleExpired, "auto-lock expired immediately after arming");
                    Check.True(session.TimeUntilAutoLock.HasValue, "no remaining time was reported");
                }
            });
        }

        private static void MasterPasswordChange(string scratchDir)
        {
            Check.Case("changing the master password re-encrypts the vault", delegate
            {
                string path = Path_(scratchDir, "session-rotate");
                if (File.Exists(path)) File.Delete(path);

                using (VaultSession session = new VaultSession(path))
                {
                    session.Unlock("original-password");
                    Credential c = session.CreateCredential();
                    c.Name = "Rotated";
                    c.Password = "unchanged-secret";
                    session.Commit(c);

                    UnlockOutcome bad = session.ChangeMasterPassword("wrong-password", "brand-new-password");
                    Check.False(bad.Success, "a wrong current password must not rotate the vault");

                    UnlockOutcome good = session.ChangeMasterPassword("original-password", "brand-new-password");
                    Check.True(good.Success, "rotation failed: " + good.Message);
                }

                using (VaultSession oldPassword = new VaultSession(path))
                {
                    Check.False(oldPassword.Unlock("original-password").Success,
                        "the old master password still opens the vault");
                }

                using (VaultSession newPassword = new VaultSession(path))
                {
                    Check.True(newPassword.Unlock("brand-new-password").Success,
                        "the new master password does not open the vault");
                    Check.AreEqual("unchanged-secret", newPassword.Credentials[0].Password,
                        "the stored secret was lost during rotation");
                }
            });
        }
    }
}
