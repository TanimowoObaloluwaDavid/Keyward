using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Keyward.Core.Storage;

namespace Keyward.Core.Domain
{
    /// <summary>What kind of record an item is.</summary>
    public enum ItemKind
    {
        Credential = 0,
        SecureNote = 1
    }

    /// <summary>Fields shared by everything stored in the vault.</summary>
    public abstract class VaultItem
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private List<string> _tags = new List<string>();
        private string _folderId = string.Empty;
        private bool _favorite;
        private DateTime _createdUtc;
        private DateTime _modifiedUtc;
        private DateTime? _lastAccessedUtc;

        protected VaultItem()
        {
            _createdUtc = DateTime.UtcNow;
            _modifiedUtc = _createdUtc;
        }

        /// <summary>Stable identifier. Generated locally; never leaves the machine.</summary>
        public string Id
        {
            get { return _id; }
            set { _id = value ?? string.Empty; }
        }

        /// <summary>Display name, for example "GitHub" or "Wifi recovery codes".</summary>
        public string Name
        {
            get { return _name; }
            set { _name = value ?? string.Empty; }
        }

        public List<string> Tags
        {
            get { return _tags; }
            set { _tags = value ?? new List<string>(); }
        }

        /// <summary>Folder this item belongs to, or empty for the root.</summary>
        public string FolderId
        {
            get { return _folderId; }
            set { _folderId = value ?? string.Empty; }
        }

        public bool Favorite
        {
            get { return _favorite; }
            set { _favorite = value; }
        }

        public DateTime CreatedUtc
        {
            get { return _createdUtc; }
            set { _createdUtc = value; }
        }

        public DateTime ModifiedUtc
        {
            get { return _modifiedUtc; }
            set { _modifiedUtc = value; }
        }

        /// <summary>Last time the item was opened. Used to drive the "recent" view.</summary>
        public DateTime? LastAccessedUtc
        {
            get { return _lastAccessedUtc; }
            set { _lastAccessedUtc = value; }
        }

        public abstract ItemKind Kind { get; }

        /// <summary>Sub-title shown under the name in list views (username, or a note marker).</summary>
        public abstract string SecondaryLabel { get; }

        /// <summary>Sub-title for the list view, which is richer for notes than for logins.</summary>
        public abstract string ListSubtitle { get; }

        /// <summary>First character used for the coloured monogram in the list.</summary>
        public virtual string Monogram
        {
            get
            {
                string source = string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim();
                return source.Substring(0, 1).ToUpperInvariant();
            }
        }

        public virtual void SerializeInto(JsonObject obj)
        {
            obj.Set("id", Id);
            obj.Set("name", Name);
            obj.Set("kind", Kind == ItemKind.SecureNote ? "note" : "login");
            obj.Set("folderId", FolderId);
            obj.Set("favorite", Favorite);
            obj.Set("createdUtc", CreatedUtc.ToString("o", CultureInfo.InvariantCulture));
            obj.Set("modifiedUtc", ModifiedUtc.ToString("o", CultureInfo.InvariantCulture));
            if (LastAccessedUtc.HasValue)
                obj.Set("lastAccessedUtc", LastAccessedUtc.Value.ToString("o", CultureInfo.InvariantCulture));

            JsonArray tags = new JsonArray();
            foreach (string tag in Tags) tags.Add(new JsonString(tag));
            obj.Set("tags", tags);
        }

        public virtual void DeserializeFrom(JsonObject obj)
        {
            Id = obj.GetString("id", string.Empty);
            Name = obj.GetString("name", string.Empty);
            FolderId = obj.GetString("folderId", string.Empty);
            Favorite = obj.GetBool("favorite", false);
            CreatedUtc = ParseUtc(obj.GetString("createdUtc", null), DateTime.UtcNow);
            ModifiedUtc = ParseUtc(obj.GetString("modifiedUtc", null), _createdUtc);
            LastAccessedUtc = ParseUtcOrNull(obj.GetString("lastAccessedUtc", null));

            Tags = new List<string>();
            JsonArray tags = obj.GetArray("tags");
            if (tags != null)
            {
                foreach (JsonValue v in tags)
                {
                    JsonString s = v as JsonString;
                    if (s != null && s.Value.Length > 0) Tags.Add(s.Value);
                }
            }
        }

        internal static DateTime ParseUtc(string text, DateTime fallback)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out parsed))
                return parsed;
            return fallback;
        }

        internal static DateTime? ParseUtcOrNull(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                return parsed;
            return null;
        }
    }

    /// <summary>A username / password record.</summary>
    public sealed class Credential : VaultItem
    {
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _url = string.Empty;
        private string _notes = string.Empty;

        public override ItemKind Kind { get { return ItemKind.Credential; } }

        public string Username
        {
            get { return _username; }
            set { _username = value ?? string.Empty; }
        }

        /// <summary>The secret. Never logged, never exported without explicit confirmation.</summary>
        public string Password
        {
            get { return _password; }
            set { _password = value ?? string.Empty; }
        }

        public string Url
        {
            get { return _url; }
            set { _url = value ?? string.Empty; }
        }

        public string Notes
        {
            get { return _notes; }
            set { _notes = value ?? string.Empty; }
        }

        public override string SecondaryLabel { get { return _username; } }

        public override string ListSubtitle
        {
            get { return string.IsNullOrEmpty(_username) ? "No username" : _username; }
        }

        /// <summary>
        /// Host name extracted from <see cref="Url"/>, used for the list-view monogram.
        /// Falls back to the first character of the name.
        /// </summary>
        public string MonogramSource
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Url))
                {
                    string host = Url.Trim();
                    int scheme = host.IndexOf("://", StringComparison.Ordinal);
                    if (scheme >= 0) host = host.Substring(scheme + 3);
                    int slash = host.IndexOfAny(new char[] { '/', '?', '#' });
                    if (slash >= 0) host = host.Substring(0, slash);
                    int at = host.LastIndexOf('@');
                    if (at >= 0 && at < host.Length - 1) host = host.Substring(at + 1);
                    if (!string.IsNullOrWhiteSpace(host)) return host;
                }
                return string.IsNullOrWhiteSpace(Name) ? "?" : Name;
            }
        }

        public override string Monogram
        {
            get
            {
                string source = MonogramSource;
                return string.IsNullOrEmpty(source) ? "?" : source.Substring(0, 1).ToUpperInvariant();
            }
        }

        public override void SerializeInto(JsonObject obj)
        {
            base.SerializeInto(obj);
            obj.Set("username", Username);
            obj.Set("password", Password);
            obj.Set("url", Url);
            obj.Set("notes", Notes);
        }

        public override void DeserializeFrom(JsonObject obj)
        {
            base.DeserializeFrom(obj);
            Username = obj.GetString("username", string.Empty);
            Password = obj.GetString("password", string.Empty);
            Url = obj.GetString("url", string.Empty);
            Notes = obj.GetString("notes", string.Empty);
        }

        public Credential Clone()
        {
            Credential copy = new Credential();
            copy.Id = Id;
            copy.Name = Name;
            copy.Username = Username;
            copy.Password = Password;
            copy.Url = Url;
            copy.Notes = Notes;
            copy.FolderId = FolderId;
            copy.Favorite = Favorite;
            copy.CreatedUtc = CreatedUtc;
            copy.ModifiedUtc = ModifiedUtc;
            copy.LastAccessedUtc = LastAccessedUtc;
            copy.Tags = new List<string>(Tags);
            return copy;
        }
    }

    /// <summary>A free-form encrypted note.</summary>
    public sealed class SecureNote : VaultItem
    {
        private string _body = string.Empty;

        public override ItemKind Kind { get { return ItemKind.SecureNote; } }

        /// <summary>The note text. Treated with the same protections as a password.</summary>
        public string Body
        {
            get { return _body; }
            set { _body = value ?? string.Empty; }
        }

        public override string SecondaryLabel { get { return "Secure note"; } }

        public override string ListSubtitle
        {
            get { return string.IsNullOrWhiteSpace(Body) ? "Empty note" : Preview; }
        }

        /// <summary>First line of the body, used as list-view preview text.</summary>
        public string Preview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Body)) return string.Empty;
                string flat = Body.Replace("\r", " ").Replace("\n", " ").Trim();
                const int MaxLength = 90;
                return flat.Length <= MaxLength ? flat : flat.Substring(0, MaxLength - 1) + "\u2026";
            }
        }

        public override void SerializeInto(JsonObject obj)
        {
            base.SerializeInto(obj);
            obj.Set("body", Body);
        }

        public override void DeserializeFrom(JsonObject obj)
        {
            base.DeserializeFrom(obj);
            Body = obj.GetString("body", string.Empty);
        }

        public SecureNote Clone()
        {
            SecureNote copy = new SecureNote();
            copy.Id = Id;
            copy.Name = Name;
            copy.Body = Body;
            copy.FolderId = FolderId;
            copy.Favorite = Favorite;
            copy.CreatedUtc = CreatedUtc;
            copy.ModifiedUtc = ModifiedUtc;
            copy.LastAccessedUtc = LastAccessedUtc;
            copy.Tags = new List<string>(Tags);
            return copy;
        }
    }

    /// <summary>A folder used to group vault items.</summary>
    public sealed class Folder
    {
        private string _id = string.Empty;
        private string _name = string.Empty;

        public Folder() { }

        public Folder(string id, string name)
        {
            _id = id ?? string.Empty;
            _name = name ?? string.Empty;
        }

        public string Id
        {
            get { return _id; }
            set { _id = value ?? string.Empty; }
        }

        public string Name
        {
            get { return _name; }
            set { _name = value ?? string.Empty; }
        }
    }

    /// <summary>
    /// The complete decrypted contents of the vault. This object only exists while the vault is
    /// unlocked, and is disposed (cleared) on lock.
    /// </summary>
    public sealed class VaultDocument
    {
        /// <summary>On-disk schema version. Bumped only for breaking layout changes.</summary>
        public const int CurrentSchemaVersion = 2;

        public List<Credential> Credentials = new List<Credential>();
        public List<SecureNote> Notes = new List<SecureNote>();
        public List<Folder> Folders = new List<Folder>();
        public DateTime ModifiedUtc = DateTime.UtcNow;

        public int ItemCount { get { return Credentials.Count + Notes.Count; } }

        /// <summary>Every distinct tag currently in use, sorted alphabetically.</summary>
        public List<string> AllTags()
        {
            SortedSet<string> set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Credential c in Credentials)
                foreach (string t in c.Tags) if (!string.IsNullOrWhiteSpace(t)) set.Add(t.Trim());
            foreach (SecureNote n in Notes)
                foreach (string t in n.Tags) if (!string.IsNullOrWhiteSpace(t)) set.Add(t.Trim());
            return new List<string>(set);
        }

        public JsonObject Serialize()
        {
            JsonObject root = new JsonObject();
            root.Set("schema", CurrentSchemaVersion);
            root.Set("modifiedUtc", ModifiedUtc.ToString("o", CultureInfo.InvariantCulture));

            JsonArray logins = new JsonArray();
            foreach (Credential c in Credentials)
            {
                JsonObject o = new JsonObject();
                c.SerializeInto(o);
                logins.Add(o);
            }
            root.Set("credentials", logins);

            JsonArray notes = new JsonArray();
            foreach (SecureNote n in Notes)
            {
                JsonObject o = new JsonObject();
                n.SerializeInto(o);
                notes.Add(o);
            }
            root.Set("notes", notes);

            JsonArray folders = new JsonArray();
            foreach (Folder f in Folders)
            {
                JsonObject o = new JsonObject();
                o.Set("id", f.Id);
                o.Set("name", f.Name);
                folders.Add(o);
            }
            root.Set("folders", folders);

            return root;
        }

        public static VaultDocument Deserialize(JsonObject root)
        {
            if (root == null) throw new FormatException("Vault payload is empty.");

            int schema = root.GetInt("schema", 1);
            if (schema > CurrentSchemaVersion)
                throw new NotSupportedException("This vault was written by a newer version of Keyward (schema " + schema + ").");

            VaultDocument doc = new VaultDocument();
            doc.ModifiedUtc = VaultItem.ParseUtc(root.GetString("modifiedUtc", null), DateTime.UtcNow);

            JsonArray creds = root.GetArray("credentials");
            if (creds != null)
            {
                foreach (JsonValue v in creds)
                {
                    JsonObject o = v as JsonObject;
                    if (o == null) throw new FormatException("Vault contains a malformed credential entry.");
                    Credential c = new Credential();
                    c.DeserializeFrom(o);
                    if (string.IsNullOrEmpty(c.Id)) c.Id = NewId();
                    doc.Credentials.Add(c);
                }
            }

            JsonArray notes = root.GetArray("notes");
            if (notes != null)
            {
                foreach (JsonValue v in notes)
                {
                    JsonObject o = v as JsonObject;
                    if (o == null) throw new FormatException("Vault contains a malformed note entry.");
                    SecureNote n = new SecureNote();
                    n.DeserializeFrom(o);
                    if (string.IsNullOrEmpty(n.Id)) n.Id = NewId();
                    doc.Notes.Add(n);
                }
            }

            JsonArray folders = root.GetArray("folders");
            if (folders != null)
            {
                foreach (JsonValue v in folders)
                {
                    JsonObject o = v as JsonObject;
                    if (o == null) continue;
                    doc.Folders.Add(new Folder(o.GetString("id", string.Empty), o.GetString("name", string.Empty)));
                }
            }

            return doc;
        }

        /// <summary>Generates a random 128-bit identifier as lowercase hex.</summary>
        public static string NewId()
        {
            byte[] bytes = Crypto.SecureRandom.NextBytes(16);
            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
