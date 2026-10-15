using System;
using System.Globalization;
using System.IO;
using Keyward.Core.Storage;

namespace Keyward.Core.Services
{
    public enum AppTheme
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    /// <summary>
    /// User preferences. None of these are secret, so they are stored as plain JSON outside the
    /// encrypted vault.
    /// </summary>
    public sealed class Settings
    {
        private AppTheme _theme = AppTheme.Dark;
        private int _autoLockMinutes = 5;
        private int _clipboardClearSeconds = 30;
        private bool _clearClipboard = true;
        private bool _lockOnExit = true;
        private bool _lockOnSleep = true;
        private bool _showPasswordByDefault;
        private bool _confirmBeforeDelete = true;
        private bool _warnOnReusedPasswords = true;
        private string _vaultPath;
        private bool _minimiseToTrayOnClose;
        private bool _hasCompletedOnboarding;

        public AppTheme Theme
        {
            get { return _theme; }
            set { _theme = value; }
        }

        /// <summary>Minutes of inactivity before the vault locks. 0 disables auto-lock.</summary>
        public int AutoLockMinutes
        {
            get { return _autoLockMinutes; }
            set { _autoLockMinutes = value < 0 ? 0 : value; }
        }

        /// <summary>Seconds after which a copied secret is cleared from the clipboard. 0 = never.</summary>
        public int ClipboardClearSeconds
        {
            get { return _clipboardClearSeconds; }
            set { _clipboardClearSeconds = value < 0 ? 0 : value; }
        }

        public bool ClearClipboard
        {
            get { return _clearClipboard; }
            set { _clearClipboard = value; }
        }

        public bool LockOnExit
        {
            get { return _lockOnExit; }
            set { _lockOnExit = value; }
        }

        /// <summary>Lock immediately when the machine resumes from sleep.</summary>
        public bool LockOnSleep
        {
            get { return _lockOnSleep; }
            set { _lockOnSleep = value; }
        }

        /// <summary>Secrets stay masked unless the user reveals them.</summary>
        public bool ShowPasswordByDefault
        {
            get { return _showPasswordByDefault; }
            set { _showPasswordByDefault = value; }
        }

        public bool ConfirmBeforeDelete
        {
            get { return _confirmBeforeDelete; }
            set { _confirmBeforeDelete = value; }
        }

        /// <summary>Flag reused and weak passwords during onboarding and entry editing.</summary>
        public bool WarnOnReusedPasswords
        {
            get { return _warnOnReusedPasswords; }
            set { _warnOnReusedPasswords = value; }
        }

        public string VaultPath
        {
            get
            {
                if (string.IsNullOrEmpty(_vaultPath)) return AppPaths.DefaultVaultPath;
                return _vaultPath;
            }
            set { _vaultPath = value; }
        }

        public bool MinimiseToTrayOnClose
        {
            get { return _minimiseToTrayOnClose; }
            set { _minimiseToTrayOnClose = value; }
        }

        public bool HasCompletedOnboarding
        {
            get { return _hasCompletedOnboarding; }
            set { _hasCompletedOnboarding = value; }
        }

        public static Settings Load()
        {
            string path = AppPaths.SettingsPath;
            if (!File.Exists(path)) return new Settings();

            try
            {
                JsonObject root = JsonValue.Parse(File.ReadAllText(path)) as JsonObject;
                if (root == null) return new Settings();

                Settings s = new Settings();
                s._theme = (AppTheme)root.GetInt("theme", (int)AppTheme.Dark);
                s._autoLockMinutes = root.GetInt("autoLockMinutes", 5);
                s._clipboardClearSeconds = root.GetInt("clipboardClearSeconds", 30);
                s._clearClipboard = root.GetBool("clearClipboard", true);
                s._lockOnExit = root.GetBool("lockOnExit", true);
                s._lockOnSleep = root.GetBool("lockOnSleep", true);
                s._showPasswordByDefault = root.GetBool("showPasswordByDefault", false);
                s._confirmBeforeDelete = root.GetBool("confirmBeforeDelete", true);
                s._warnOnReusedPasswords = root.GetBool("warnOnReusedPasswords", true);
                s._vaultPath = root.GetString("vaultPath", string.Empty);
                s._minimiseToTrayOnClose = root.GetBool("minimiseToTrayOnClose", false);
                s._hasCompletedOnboarding = root.GetBool("hasCompletedOnboarding", false);

                if (s._autoLockMinutes < 0) s._autoLockMinutes = 0;
                if (s._clipboardClearSeconds < 0) s._clipboardClearSeconds = 0;
                if (!Enum.IsDefined(typeof(AppTheme), s._theme)) s._theme = AppTheme.Dark;

                return s;
            }
            catch (Exception)
            {
                // A corrupt settings file must never stop the app from starting. Fall back to
                // defaults rather than refusing to launch.
                return new Settings();
            }
        }

        public void Save()
        {
            try
            {
                AppPaths.EnsureDataDirectory();

                JsonObject root = new JsonObject();
                root.Set("theme", (int)_theme);
                root.Set("autoLockMinutes", _autoLockMinutes);
                root.Set("clipboardClearSeconds", _clipboardClearSeconds);
                root.Set("clearClipboard", _clearClipboard);
                root.Set("lockOnExit", _lockOnExit);
                root.Set("lockOnSleep", _lockOnSleep);
                root.Set("showPasswordByDefault", _showPasswordByDefault);
                root.Set("confirmBeforeDelete", _confirmBeforeDelete);
                root.Set("warnOnReusedPasswords", _warnOnReusedPasswords);
                root.Set("vaultPath", _vaultPath ?? string.Empty);
                root.Set("minimiseToTrayOnClose", _minimiseToTrayOnClose);
                root.Set("hasCompletedOnboarding", _hasCompletedOnboarding);

                AtomicFile.WriteAllBytesAtomic(AppPaths.SettingsPath,
                    System.Text.Encoding.UTF8.GetBytes(root.ToPrettyString()));
            }
            catch (Exception)
            {
                // Preferences are a convenience. Losing them is not worth interrupting the user.
            }
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Auto-lock after {0} min; clipboard cleared after {1} s", _autoLockMinutes, _clipboardClearSeconds);
        }
    }
}
