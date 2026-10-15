using System;
using System.Collections.Generic;
using System.IO;
using Keyward.Core.Crypto;
using Keyward.Core.Domain;
using Keyward.Core.Storage;

namespace Keyward.Core.Services
{
    /// <summary>Result of an unlock attempt, carrying a reason the UI can act on.</summary>
    public sealed class UnlockOutcome
    {
        public bool Success;
        public bool VaultCreated;
        public string Message;
        public VaultOpenFailure Failure;

        public static UnlockOutcome Ok(bool created)
        {
            return new UnlockOutcome { Success = true, VaultCreated = created, Failure = VaultOpenFailure.None };
        }

        public static UnlockOutcome Error(string message, VaultOpenFailure failure)
        {
            return new UnlockOutcome { Success = false, Message = message, Failure = failure };
        }
    }

    /// <summary>
    /// Owns the decrypted vault and the derived key for the duration of an unlocked session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single place in the application where key material lives. Everything else -
    /// the WPF layer, the search box, the list controls - only ever sees decrypted
    /// <see cref="VaultItem"/> objects, never a key. Calling <see cref="Lock"/> disposes the
    /// <see cref="MasterKey"/>, which zeroes both sub-keys, and scrubs the document.
    /// </para>
    /// <para>
    /// Every mutating call marks the session dirty and persists immediately. A password manager
    /// that holds an unsaved change in memory and loses it on a crash is worse than useless, so
    /// there is no deferred-save mode; writes go through the atomic replace in
    /// <see cref="AtomicFile"/> and are therefore all-or-nothing.
    /// </para>
    /// <para>
    /// All public members are safe to call from the UI thread. Cross-thread use is serialised on a
    /// private lock so a background auto-lock timer cannot interleave with an edit.
    /// </para>
    /// </remarks>
    public sealed class VaultSession : IDisposable
    {
        private readonly object _gate = new object();

        private MasterKey _key;
        private KdfParameters _parameters;
        private VaultDocument _document;
        private string _path;
        private bool _dirty;
        private bool _disposed;

        private DateTime _lastActivityUtc = DateTime.UtcNow;
        private DateTime? _idleDeadlineUtc;

        /// <summary>Raised after any change to the vault contents.</summary>
        public event EventHandler Changed;

        /// <summary>Raised when the session locks, after keys have been destroyed.</summary>
        public event EventHandler Locked;

        public VaultSession(string vaultPath)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentException("Vault path is required.", "vaultPath");
            _path = vaultPath;
        }

        public string VaultPath
        {
            get { lock (_gate) { return _path; } }
        }

        public bool IsUnlocked
        {
            get { lock (_gate) { return _key != null && _document != null; } }
        }

        public bool IsDirty
        {
            get { lock (_gate) { return _dirty; } }
        }

        /// <summary>True when a vault file already exists on disk.</summary>
        public bool VaultExists
        {
            get { return File.Exists(VaultPath); }
        }

        /// <summary>
        /// Opens an existing vault, or creates one when no file exists yet.
        /// </summary>
        public UnlockOutcome Unlock(string masterPassword)
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (string.IsNullOrEmpty(masterPassword))
                    return UnlockOutcome.Error("Enter your master password.", VaultOpenFailure.WrongPasswordOrTampered);

                bool creating = !File.Exists(_path);

                if (creating)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_path));
                        AppPaths.RestrictDirectory(Path.GetDirectoryName(_path));

                        _parameters = new KdfParameters();
                        _key = KeyDerivation.Derive(masterPassword, _parameters);
                        _document = new VaultDocument();

                        // Write immediately. Without this the new vault exists only in memory: if
                        // the user closes the app before adding anything, the vault file is never
                        // created and the next launch asks them to create one again, silently
                        // abandoning the master password they just chose.
                        Save(true);

                        Touch();
                        RaiseChanged();
                        return UnlockOutcome.Ok(true);
                    }
                    catch (Exception ex)
                    {
                        LockInternal();
                        return UnlockOutcome.Error(
                            "The vault could not be created. " + ex.Message, VaultOpenFailure.FileUnreadable);
                    }
                }

                VaultOpenResult result = VaultFile.Open(_path, masterPassword);
                if (!result.Success)
                    return UnlockOutcome.Error(result.Message, result.Failure);

                _key = result.Key;
                _parameters = result.Parameters;
                _document = result.Document;
                _dirty = false;
                Touch();
                RaiseChanged();
                return UnlockOutcome.Ok(false);
            }
        }

        /// <summary>Destroys the keys and scrubs the decrypted document.</summary>
        public void Lock()
        {
            bool wasUnlocked;
            lock (_gate)
            {
                wasUnlocked = _key != null;
                LockInternal();
            }

            if (wasUnlocked)
            {
                EventHandler handler = Locked;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private void LockInternal()
        {
            if (_key != null)
            {
                _key.Dispose();
                _key = null;
            }

            if (_document != null)
            {
                Scrub(_document);
                _document = null;
            }

            _parameters = null;
            _dirty = false;
            _idleDeadlineUtc = null;
        }

        /// <summary>Overwrites every secret string in the document before releasing it.</summary>
        private static void Scrub(VaultDocument doc)
        {
            foreach (Credential c in doc.Credentials)
            {
                if (c.Password != null) c.Password = new string('\0', c.Password.Length);
                if (c.Notes != null) c.Notes = string.Empty;
            }
            foreach (SecureNote n in doc.Notes)
            {
                if (n.Body != null) n.Body = string.Empty;
            }
            doc.Credentials.Clear();
            doc.Notes.Clear();
            doc.Folders.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("VaultSession");
        }

        private VaultDocument RequireDocument()
        {
            ThrowIfDisposed();
            if (_document == null)
                throw new InvalidOperationException("The vault is locked.");
            return _document;
        }

        // ---- persistence --------------------------------------------------------------------

        /// <summary>Writes the vault if it has unsaved changes, or unconditionally when forced.</summary>
        public void Save(bool force)
        {
            lock (_gate)
            {
                VaultDocument doc = RequireDocument();
                if (!force && !_dirty) return;

                VaultFile.Write(_path, _key, _parameters, doc);
                _dirty = false;
            }
        }

        private void Touch()
        {
            _lastActivityUtc = DateTime.UtcNow;
        }

        private void MarkChanged()
        {
            _dirty = true;
            Touch();
            Save(false);
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ---- credentials ---------------------------------------------------------------------

        public List<Credential> Credentials
        {
            get { lock (_gate) { return new List<Credential>(RequireDocument().Credentials); } }
        }

        public List<SecureNote> Notes
        {
            get { lock (_gate) { return new List<SecureNote>(RequireDocument().Notes); } }
        }

        public List<Folder> Folders
        {
            get { lock (_gate) { return new List<Folder>(RequireDocument().Folders); } }
        }

        public List<string> AllTags
        {
            get { lock (_gate) { return RequireDocument().AllTags(); } }
        }

        /// <summary>Creates a new credential with sensible defaults and saves.</summary>
        public Credential CreateCredential()
        {
            lock (_gate)
            {
                VaultDocument doc = RequireDocument();
                Credential c = new Credential();
                c.Id = VaultDocument.NewId();
                c.Name = "New login";
                doc.Credentials.Add(c);
                MarkChanged();
                return c;
            }
        }

        public SecureNote CreateNote()
        {
            lock (_gate)
            {
                VaultDocument doc = RequireDocument();
                SecureNote n = new SecureNote();
                n.Id = VaultDocument.NewId();
                n.Name = "New secure note";
                doc.Notes.Add(n);
                MarkChanged();
                return n;
            }
        }

        /// <summary>Persists an edited item and refreshes its modified timestamp.</summary>
        public void Commit(VaultItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            lock (_gate)
            {
                RequireDocument();
                item.ModifiedUtc = DateTime.UtcNow;
                MarkChanged();
            }
        }

        /// <summary>Records that an item was opened, so it can appear in the recent list.</summary>
        public void MarkAccessed(VaultItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            lock (_gate)
            {
                RequireDocument();
                item.LastAccessedUtc = DateTime.UtcNow;
                Touch();
                // Deliberately not MarkChanged: access timestamps are not worth a disk write for
                // every click, and they are not security relevant.
                RaiseChanged();
            }
        }

        public void Delete(VaultItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            lock (_gate)
            {
                VaultDocument doc = RequireDocument();

                bool removed = false;
                if (item is Credential)
                {
                    Credential c = (Credential)item;
                    for (int i = 0; i < doc.Credentials.Count; i++)
                        if (ReferenceEquals(doc.Credentials[i], c) || doc.Credentials[i].Id == c.Id)
                        {
                            doc.Credentials.RemoveAt(i);
                            removed = true;
                            break;
                        }
                }
                else if (item is SecureNote)
                {
                    SecureNote n = (SecureNote)item;
                    for (int i = 0; i < doc.Notes.Count; i++)
                        if (ReferenceEquals(doc.Notes[i], n) || doc.Notes[i].Id == n.Id)
                        {
                            doc.Notes[i].Body = string.Empty;
                            doc.Notes.RemoveAt(i);
                            removed = true;
                            break;
                        }
                }

                if (!removed)
                    throw new InvalidOperationException("That item is no longer in the vault.");

                MarkChanged();
            }
        }

        public void ToggleFavorite(VaultItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            lock (_gate)
            {
                RequireDocument();
                item.Favorite = !item.Favorite;
                MarkChanged();
            }
        }

        public Folder CreateFolder(string name)
        {
            lock (_gate)
            {
                VaultDocument doc = RequireDocument();
                Folder f = new Folder(VaultDocument.NewId(), name);
                doc.Folders.Add(f);
                MarkChanged();
                return f;
            }
        }

        public void DeleteFolder(string folderId)
        {
            lock (_gate)
            {
                VaultDocument doc = RequireDocument();
                doc.Folders.RemoveAll(delegate(Folder f) { return f.Id == folderId; });

                // Items in a deleted folder fall back to the root rather than disappearing.
                foreach (Credential c in doc.Credentials)
                    if (c.FolderId == folderId) c.FolderId = string.Empty;
                foreach (SecureNote n in doc.Notes)
                    if (n.FolderId == folderId) n.FolderId = string.Empty;

                MarkChanged();
            }
        }

        /// <summary>Re-derives the key under a new master password and rewrites the vault.</summary>
        public UnlockOutcome ChangeMasterPassword(string currentPassword, string newPassword)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_document == null || _key == null)
                    return UnlockOutcome.Error("The vault is locked.", VaultOpenFailure.None);

                if (string.IsNullOrEmpty(newPassword))
                    return UnlockOutcome.Error("Choose a new master password.", VaultOpenFailure.None);

                // Prove the current password really opens this vault before writing a new one over
                // it. Without this check a typo could destroy access to the only copy.
                VaultOpenResult verify = VaultFile.Open(_path, currentPassword);
                if (!verify.Success)
                    return UnlockOutcome.Error("Your current master password is not correct.", verify.Failure);

                try
                {
                    KdfParameters newParameters = new KdfParameters();
                    MasterKey newKey = KeyDerivation.Derive(newPassword, newParameters);
                    try
                    {
                        VaultFile.Write(_path, newKey, newParameters, _document);
                    }
                    catch
                    {
                        newKey.Dispose();
                        throw;
                    }

                    _key.Dispose();
                    _key = newKey;
                    _parameters = newParameters;
                    _dirty = false;
                    Touch();
                    RaiseChanged();
                    return UnlockOutcome.Ok(false);
                }
                catch (Exception ex)
                {
                    return UnlockOutcome.Error("The master password could not be changed. " + ex.Message,
                        VaultOpenFailure.FileUnreadable);
                }
                finally
                {
                    if (verify.Key != null) verify.Key.Dispose();
                }
            }
        }

        /// <summary>
        /// Changes the vault file location by writing the current contents to a new file and
        /// verifying it can be reopened, so a bad path cannot strand the user.
        /// </summary>
        public UnlockOutcome MoveTo(string newPath)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_document == null || _key == null)
                    return UnlockOutcome.Error("The vault is locked.", VaultOpenFailure.None);

                if (string.IsNullOrEmpty(newPath))
                    return UnlockOutcome.Error("Choose a destination for the vault file.", VaultOpenFailure.None);

                try
                {
                    string directory = Path.GetDirectoryName(newPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                        AppPaths.RestrictDirectory(directory);
                    }

                    VaultFile.Write(newPath, _key, _parameters, _document);

                    // Prove the new copy is readable before adopting its path. A write that reports
                    // success but cannot be reopened would leave the user with no working vault.
                    string problem;
                    if (!VaultFile.Verify(newPath, _key, _parameters, out problem))
                    {
                        return UnlockOutcome.Error(
                            "The vault was written but could not be read back, so the location was not changed. " + problem,
                            VaultOpenFailure.FileUnreadable);
                    }

                    _path = newPath;
                    _dirty = false;
                    RaiseChanged();
                    return UnlockOutcome.Ok(false);
                }
                catch (Exception ex)
                {
                    return UnlockOutcome.Error("The vault could not be moved. " + ex.Message,
                        VaultOpenFailure.FileUnreadable);
                }
            }
        }

        /// <summary>Counts how many other items share this item's password.</summary>
        public int CountPasswordReuse(VaultItem item)
        {
            lock (_gate)
            {
                Credential c = item as Credential;
                if (c == null || string.IsNullOrEmpty(c.Password)) return 0;

                int count = 0;
                foreach (Credential other in RequireDocument().Credentials)
                    if (!ReferenceEquals(other, c) &&
                        AuthenticatedCipher.FixedTimeEqualsUtf8(c.Password, other.Password))
                        count++;

                return count;
            }
        }

        // ---- auto-lock -----------------------------------------------------------------------

        /// <summary>
        /// Arms the idle timer. A setting of 0 disables it.
        /// </summary>
        public void ArmAutoLock(int minutes)
        {
            lock (_gate)
            {
                _lastActivityUtc = DateTime.UtcNow;
                _idleDeadlineUtc = minutes > 0
                    ? (DateTime?)(_lastActivityUtc.AddMinutes(minutes))
                    : null;
            }
        }

        /// <summary>True when the idle timeout has elapsed and the vault should lock.</summary>
        public bool IsIdleExpired
        {
            get
            {
                lock (_gate)
                {
                    if (_key == null) return false;
                    if (!_idleDeadlineUtc.HasValue) return false;
                    return DateTime.UtcNow >= _idleDeadlineUtc.Value;
                }
            }
        }

        /// <summary>Remaining idle time, or null when auto-lock is off.</summary>
        public TimeSpan? TimeUntilAutoLock
        {
            get
            {
                lock (_gate)
                {
                    if (!_idleDeadlineUtc.HasValue) return null;
                    TimeSpan remaining = _idleDeadlineUtc.Value - DateTime.UtcNow;
                    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                }
            }
        }

        /// <summary>Records user activity, pushing the idle deadline back.</summary>
        public void RegisterActivity()
        {
            lock (_gate) { Touch(); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                LockInternal();
            }
            GC.KeepAlive(this);
        }
    }
}
