using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Keyward.Core.Crypto;
using Keyward.Core.Domain;
using Keyward.Core.Services;
using Keyward.Ui.Design;

namespace Keyward.Ui.Screens
{
    /// <summary>Which slice of the vault the list is showing.</summary>
    public enum VaultFilter
    {
        All = 0,
        Favorites = 1,
        Recent = 2,
        Logins = 3,
        Notes = 4,
        Folder = 5,
        Tag = 6
    }

    /// <summary>
    /// The unlocked workspace: navigation, the item list, and the detail pane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three-region layout (rail, list, detail) is the right shape for a desktop password
    /// manager because it keeps the list, the selection and the detail visible at the same time,
    /// so opening an item never costs a navigation. Below the narrow breakpoint the detail pane
    /// takes over the window and the list is shown as a back action, because three columns on a
    /// small window leave each one too narrow to be usable.
    /// </para>
    /// <para>
    /// Secrets are masked by default everywhere, including the detail pane. Revealing is an
    /// explicit, per-item action rather than a global toggle.
    /// </para>
    /// </remarks>
    public sealed class VaultView
    {
        private readonly MainWindow _window;
        private readonly VaultSession _session;

        private TextBox _searchBox;
        private ListBox _listBox;
        private ContentControl _detailHost;
        private TextBlock _listTitle;
        private TextBlock _listCount;
        private StackPanel _emptyState;

        private VaultFilter _filter = VaultFilter.All;
        private string _filterValue = string.Empty;
        private VaultItem _selected;
        private bool _revealSelected;
        private string _pendingSearch;

        public VaultView(MainWindow window)
        {
            _window = window;
            _session = window.Session;
            _session.Changed += delegate { DispatchRebuild(); };
        }

        private void DispatchRebuild()
        {
            if (!_window.Dispatcher.CheckAccess()) { _window.Dispatcher.BeginInvoke(new Action(RefreshList)); return; }
            RefreshList();
        }

        // =====================================================================================
        // Entry points used by the shell's shortcuts
        // =====================================================================================

        public UIElement Build(string search)
        {
            if (!string.IsNullOrEmpty(search)) _pendingSearch = search;
            UIElement root = Build();
            if (_listBox != null)
            {
                if (!string.IsNullOrEmpty(_pendingSearch)) FocusSearch();
                _pendingSearch = null;
            }
            return root;
        }

        public void FocusSearch()
        {
            if (_searchBox == null) return;
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        /// <summary>Clears the search box. Returns false when there was nothing to clear.</summary>
        public bool ClearSearch()
        {
            if (_searchBox == null || string.IsNullOrEmpty(_searchBox.Text)) return false;
            _searchBox.Text = string.Empty;
            FocusSearch();
            return true;
        }

        public void CreateLogin()
        {
            Credential c = _session.CreateCredential();
            _revealSelected = false;
            Select(c);
            _filter = VaultFilter.All;
            _filterValue = string.Empty;
            RefreshList();
        }

        public void CreateNote()
        {
            SecureNote n = _session.CreateNote();
            _revealSelected = false;
            Select(n);
            _filter = VaultFilter.All;
            _filterValue = string.Empty;
            RefreshList();
        }

        public void EditSelected()
        {
            if (_selected == null) return;
            _revealSelected = true;
            BuildDetail();
        }

        public void CopySelectedPassword()
        {
            Credential c = _selected as Credential;
            if (c == null)
            {
                _window.Notifications.Show("That item has no password to copy.");
                return;
            }
            CopySecret(c.Password, "Password copied");
        }

        // =====================================================================================
        // Layout
        // =====================================================================================

        private UIElement _root;

        public UIElement Build()
        {
            Theme t = Factory.Theme;

            Grid root = new Grid { Background = t.Background };
            _root = root;

            Grid shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(t.SidebarWidth) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- navigation rail ------------------------------------------------------------
            Border sidebar = new Border { Background = t.Sidebar };
            _sidebarHost = new ContentControl();
            sidebar.Child = _sidebarHost;

            Grid.SetColumn(sidebar, 0);
            shell.Children.Add(sidebar);

            // Hairline on the rail's trailing edge.
            Border railEdge = Factory.Divider(true);
            railEdge.Width = 1;
            railEdge.HorizontalAlignment = HorizontalAlignment.Right;
            railEdge.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetColumn(railEdge, 0);
            shell.Children.Add(railEdge);

            // ---- list + detail -------------------------------------------------------------
            Grid main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(t.ListWidth) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(main, 1);
            shell.Children.Add(main);

            Border listColumn = new Border { Background = t.Background };
            listColumn.Child = BuildListPane(t);
            Grid.SetColumn(listColumn, 0);
            _listColumn = listColumn;
            main.Children.Add(listColumn);

            _detailHost = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(_detailHost, 1);
            _detailColumn = _detailHost;
            main.Children.Add(_detailHost);

            root.Children.Add(shell);

            RebuildSidebar();
            RefreshList();
            root.SizeChanged += delegate { ApplyResponsiveLayout(); };
            ApplyResponsiveLayout();

            return root;
        }

        private ContentControl _sidebarHost;
        private UIElement _listColumn;
        private UIElement _detailColumn;

        /// <summary>Rebuilds the navigation rail, reflecting the current filter selection.</summary>
        private void RebuildSidebar()
        {
            Theme t = Factory.Theme;
            if (_sidebarHost == null) return;

            ScrollViewer scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            StackPanel rail = new StackPanel { Margin = new Thickness(14, 18, 14, 14) };

            rail.Children.Add(SidebarHeading(t, "Library"));
            rail.Children.Add(NavButton(t, "grid", "All items", VaultFilter.All, string.Empty));
            rail.Children.Add(NavButton(t, "key", "Logins", VaultFilter.Logins, string.Empty));
            rail.Children.Add(NavButton(t, "note", "Secure notes", VaultFilter.Notes, string.Empty));
            rail.Children.Add(NavButton(t, "star", "Favourites", VaultFilter.Favorites, string.Empty));
            rail.Children.Add(NavButton(t, "history", "Recently used", VaultFilter.Recent, string.Empty));

            rail.Children.Add(Spacer(18));
            rail.Children.Add(SidebarHeading(t, "Folders"));
            _folderAnchor = new StackPanel();
            rail.Children.Add(_folderAnchor);

            rail.Children.Add(Spacer(14));
            rail.Children.Add(SidebarHeading(t, "Tags"));
            _tagAnchor = new StackPanel();
            rail.Children.Add(_tagAnchor);

            rail.Children.Add(Spacer(18));
            rail.Children.Add(Factory.Divider());
            rail.Children.Add(Spacer(10));
            rail.Children.Add(NavButton(t, "wand", "Generator", VaultFilter.All, string.Empty, delegate { _window.GoGenerator(); }));
            rail.Children.Add(NavButton(t, "gear", "Settings", VaultFilter.All, string.Empty, delegate { _window.GoSettings(); }));

            scroller.Content = rail;
            _sidebarHost.Content = scroller;

            BuildFoldersAndTags();
        }

        private StackPanel _folderAnchor;
        private StackPanel _tagAnchor;

        private UIElement BuildListPane(Theme t)
        {
            Grid pane = new Grid();
            // Row 0 is the header, row 1 the scrolling list area. The empty state and the list
            // both live in row 1 so the empty state is centred in the content area instead of
            // stacking on top of the header.
            pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel header = new StackPanel { Margin = new Thickness(18, 18, 14, 0) };

            Grid titleRow = new Grid();
            _listTitle = Factory.Text("All items", t.TitleSize, t.TextPrimary, false);
            _listTitle.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(_listTitle);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(Factory.Button(null, ButtonKind.Ghost, CreateNote, "note", 30));
            actions.Children.Add(Factory.Button(null, ButtonKind.Primary, CreateLogin, "plus", 30));
            titleRow.Children.Add(actions);
            header.Children.Add(titleRow);

            header.Children.Add(Spacer(14));

            // Search
            Grid searchHost = new Grid();
            Grid searchCols = new Grid();
            searchCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchCols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _searchBox = Factory.TextInputWithPlaceholder("Search the vault", 36);
            AutomationProperties.SetName(_searchBox, "Search the vault");
            _searchBox.TextChanged += delegate { RefreshList(); };
            Grid.SetColumn(_searchBox, 0);
            searchCols.Children.Add(_searchBox);

            Button clearSearch = Factory.Button(null, ButtonKind.Ghost, delegate
            {
                _searchBox.Text = string.Empty;
                FocusSearch();
            }, "close", 30);
            clearSearch.Width = 30;
            clearSearch.Visibility = Visibility.Collapsed;
            _clearSearchButton = clearSearch;
            Grid.SetColumn(clearSearch, 1);
            searchCols.Children.Add(clearSearch);
            searchHost.Children.Add(searchCols);
            header.Children.Add(searchHost);

            header.Children.Add(Spacer(10));

            _listCount = Factory.Text(string.Empty, t.CaptionSize, t.TextMuted);
            header.Children.Add(_listCount);
            header.Children.Add(Spacer(8));

            pane.Children.Add(header);

            // Empty state overlays the list area when nothing matches.
            _emptyState = BuildEmptyState(t);
            _emptyState.Visibility = Visibility.Collapsed;
            Grid.SetRow(_emptyState, 1);
            pane.Children.Add(_emptyState);

            _listBox = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(2, 0, 2, 16)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
            _listBox.ItemTemplate = RowTemplate(t);
            _listBox.ItemContainerStyle = ListRowStyle(t);
            _listBox.SelectionChanged += delegate
            {
                VaultItem picked = _listBox.SelectedItem as VaultItem;
                if (picked != null)
                {
                    _selected = picked;
                    _revealSelected = false;
                    _session.MarkAccessed(picked);
                    BuildDetail();
                }
            };

            pane.Children.Add(_listBox);
            Grid.SetRow(_listBox, 1);
            return pane;
        }

        private Button _clearSearchButton;

        // =====================================================================================
        // Sidebar pieces
        // =====================================================================================

        private static UIElement Spacer(double height) { return new FrameworkElement { Height = height }; }

        /// <summary>Formats a number for XAML templates.</summary>
        private static string N(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static UIElement SidebarHeading(Theme t, string text)
        {
            TextBlock block = Factory.Label(text, t.TextMuted);
            block.Margin = new Thickness(10, 4, 0, 8);
            return block;
        }

        private UIElement NavButton(Theme t, string icon, string label, VaultFilter filter,
                                    string value, Action overrideAction = null)
        {
            bool selected = overrideAction == null && _filter == filter &&
                            string.Equals(_filterValue ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal);

            Brush foreground = selected ? t.TextPrimary : t.TextSecondary;
            Brush background = selected ? t.Selection : Brushes.Transparent;

            Button button = Factory.Button(null, ButtonKind.Ghost, delegate
            {
                if (overrideAction != null) { overrideAction(); return; }
                _filter = filter;
                _filterValue = value ?? string.Empty;
                _selected = null;
                RefreshList();
                RebuildSidebarSelection();
            }, null, 34);

            Border host = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(t.RadiusControl),
                Margin = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(10, 0, 8, 0),
                Child = button
            };

            // Rebuild the visual state inside the button's own template by composing content.
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Factory.Icon(icon, 17, selected ? t.Accent : t.TextMuted));
            row.Children.Add(new FrameworkElement { Width = 11 });
            TextBlock text = Factory.Text(label, t.BodySize, foreground, selected);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            button.Content = row;
            button.Width = double.NaN;
            button.Height = 34;
            button.Padding = new Thickness(0);
            AutomationProperties.SetName(button, label);

            return host;
        }

        private void RebuildSidebarSelection()
        {
            RebuildSidebar();
        }

        private void BuildFoldersAndTags()
        {
            Theme t = Factory.Theme;
            if (_folderAnchor != null) _folderAnchor.Children.Clear();
            if (_tagAnchor != null) _tagAnchor.Children.Clear();

            List<Folder> folders = _session.Folders;
            if (folders.Count == 0)
            {
                if (_folderAnchor != null)
                {
                    TextBlock empty = Factory.Text("No folders yet", t.CaptionSize, t.TextMuted);
                    empty.Margin = new Thickness(10, 2, 0, 6);
                    _folderAnchor.Children.Add(empty);
                }
            }
            else
            {
                foreach (Folder f in folders)
                    _folderAnchor.Children.Add(NavButton(t, "folder", f.Name, VaultFilter.Folder, f.Id));
            }

            List<string> tags = _session.AllTags;
            if (tags.Count == 0)
            {
                if (_tagAnchor != null)
                {
                    TextBlock empty = Factory.Text("No tags yet", t.CaptionSize, t.TextMuted);
                    empty.Margin = new Thickness(10, 2, 0, 6);
                    _tagAnchor.Children.Add(empty);
                }
            }
            else
            {
                foreach (string tag in tags)
                    _tagAnchor.Children.Add(NavButton(t, "tag", tag, VaultFilter.Tag, tag));
            }
        }

        // =====================================================================================
        // List population
        // =====================================================================================

        private void RefreshList()
        {
            if (_listBox == null) return;

            Theme t = Factory.Theme;
            string query = _searchBox == null ? string.Empty : (_searchBox.Text ?? string.Empty).Trim();

            if (_clearSearchButton != null)
                _clearSearchButton.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            List<VaultItem> items = Query(query);

            _listTitle.Text = CurrentFilterTitle();

            _listCount.Text = items.Count == 0
                ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, "{0} {1}",
                    items.Count, items.Count == 1 ? "item" : "items");

            _listBox.ItemsSource = null;
            _listBox.ItemsSource = items;

            bool nothing = items.Count == 0;
            if (_emptyState != null) _emptyState.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;

            // Keep the selection if it survived the filter change.
            if (_selected != null && items.Contains(_selected))
            {
                _listBox.SelectedItem = _selected;
            }
            else if (!nothing)
            {
                _listBox.SelectedIndex = 0;
            }
            else
            {
                _selected = null;
                BuildDetail();
            }

            BuildFoldersAndTags();
            if (t == null) { /* theme is always set before Refresh is called */ }
        }

        private string CurrentFilterTitle()
        {
            switch (_filter)
            {
                case VaultFilter.Favorites: return "Favourites";
                case VaultFilter.Recent: return "Recently used";
                case VaultFilter.Logins: return "Logins";
                case VaultFilter.Notes: return "Secure notes";
                case VaultFilter.Folder:
                    foreach (Folder f in _session.Folders)
                        if (f.Id == _filterValue) return f.Name;
                    return "Folder";
                case VaultFilter.Tag: return "#" + _filterValue;
                default: return "All items";
            }
        }

        /// <summary>
        /// Applies the folder/tag/scope filter and the free-text query.
        /// </summary>
        /// <remarks>
        /// The search deliberately inspects decrypted values in memory, so results update as the
        /// user types. That is the only way to search encrypted-at-rest data, and it is why the
        /// application must never write those values to a log, a crash report or an index.
        /// </remarks>
        private List<VaultItem> Query(string query)
        {
            List<VaultItem> result = new List<VaultItem>();

            foreach (Credential c in _session.Credentials)
            {
                if (Matches(c, query)) result.Add(c);
            }

            foreach (SecureNote n in _session.Notes)
            {
                if (Matches(n, query)) result.Add(n);
            }

            result.Sort(delegate(VaultItem a, VaultItem b)
            {
                // Favourites first, then most recently modified.
                if (a.Favorite != b.Favorite) return a.Favorite ? -1 : 1;
                return b.ModifiedUtc.CompareTo(a.ModifiedUtc);
            });

            if (_filter == VaultFilter.Favorites)
                result.RemoveAll(delegate(VaultItem i) { return !i.Favorite; });

            if (_filter == VaultFilter.Recent)
            {
                List<VaultItem> recent = new List<VaultItem>();
                foreach (VaultItem i in result)
                    if (i.LastAccessedUtc.HasValue) recent.Add(i);
                recent.Sort(delegate(VaultItem a, VaultItem b)
                {
                    return b.LastAccessedUtc.Value.CompareTo(a.LastAccessedUtc.Value);
                });
                result = recent;
            }

            return result;
        }

        private bool Matches(VaultItem item, string query)
        {
            if (_filter == VaultFilter.Logins && item.Kind != ItemKind.Credential) return false;
            if (_filter == VaultFilter.Notes && item.Kind != ItemKind.SecureNote) return false;
            if (_filter == VaultFilter.Folder && !string.Equals(item.FolderId, _filterValue, StringComparison.Ordinal)) return false;
            if (_filter == VaultFilter.Tag)
            {
                bool has = false;
                foreach (string tag in item.Tags)
                    if (string.Equals(tag, _filterValue, StringComparison.OrdinalIgnoreCase)) { has = true; break; }
                if (!has) return false;
            }

            if (string.IsNullOrEmpty(query)) return true;

            if (Contains(item.Name, query)) return true;
            foreach (string tag in item.Tags)
                if (Contains(tag, query)) return true;

            Credential c = item as Credential;
            if (c != null)
            {
                if (Contains(c.Username, query)) return true;
                if (Contains(c.Url, query)) return true;
            }
            else
            {
                SecureNote n = item as SecureNote;
                if (n != null && Contains(n.Preview, query)) return true;
            }

            return false;
        }

        private static bool Contains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The list-row container style.
        /// </summary>
        /// <remarks>
        /// Authored as XAML and parsed at runtime rather than assembled with
        /// <c>FrameworkElementFactory</c>, because binding support on that class arrived after the
        /// WPF reference assemblies available on this machine.
        /// </remarks>
        private Style ListRowStyle(Theme t)
        {
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "       TargetType='{x:Type ListBoxItem}'>" +
                "  <Setter Property='HorizontalContentAlignment' Value='Stretch'/>" +
                "  <Setter Property='Padding' Value='0'/>" +
                "  <Setter Property='Background' Value='Transparent'/>" +
                "  <Setter Property='Margin' Value='6,3,6,3'/>" +
                "  <Setter Property='Template'>" +
                "    <Setter.Value>" +
                "      <ControlTemplate TargetType='{x:Type ListBoxItem}'>" +
                "        <Border x:Name='bd' Background='{TemplateBinding Background}' " +
                "                CornerRadius='" + N(t.RadiusControl) + "' SnapsToDevicePixels='True'>" +
                "          <ContentPresenter HorizontalAlignment='Stretch'/>" +
                "        </Border>" +
                "        <ControlTemplate.Triggers>" +
                "          <Trigger Property='IsMouseOver' Value='True'>" +
                "            <Setter TargetName='bd' Property='Background' Value='" + ColOf(t.Hover) + "'/>" +
                "          </Trigger>" +
                "          <Trigger Property='IsSelected' Value='True'>" +
                "            <Setter TargetName='bd' Property='Background' Value='" + ColOf(t.Selection) + "'/>" +
                "          </Trigger>" +
                "        </ControlTemplate.Triggers>" +
                "      </ControlTemplate>" +
                "    </Setter.Value>" +
                "  </Setter>" +
                "</Style>";

            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static string ColOf(Brush brush)
        {
            SolidColorBrush solid = brush as SolidColorBrush;
            if (solid == null) return "#00000000";
            Color c = solid.Color;
            if (c.A == 255) return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
        }

        /// <summary>The row template. Everything binds to the item, so WPF keeps rows in sync.</summary>
        private DataTemplate RowTemplate(Theme t)
        {
            string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "            xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "  <Grid Height='" + N(t.ListRowHeight) + "'>" +
                "    <Grid.ColumnDefinitions>" +
                "      <ColumnDefinition Width='3'/>" +
                "      <ColumnDefinition Width='*'/>" +
                "      <ColumnDefinition Width='Auto'/>" +
                "    </Grid.ColumnDefinitions>" +

                "    <Border x:Name='sel' Grid.Column='0' Width='3' CornerRadius='2' Background='Transparent'/>" +

                "    <Border Grid.Column='1' Width='34' Height='34' CornerRadius='9' " +
                "            Background='" + ColOf(t.Input) + "' VerticalAlignment='Center' " +
                "            Margin='12,0,12,0'>" +
                "      <TextBlock Text='{Binding Monogram}' " +
                "                 FontFamily='" + t.UiMediumFamily.Source + "' " +
                "                 FontSize='" + N(t.BodySize) + "' " +
                "                 Foreground='" + ColOf(t.TextSecondary) + "' " +
                "                 HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                "    </Border>" +

                "    <StackPanel Grid.Column='1' VerticalAlignment='Center' Margin='58,0,8,0'>" +
                "      <TextBlock Text='{Binding Name}' " +
                "                 FontFamily='" + t.UiMediumFamily.Source + "' " +
                "                 FontSize='" + N(t.ItemNameSize) + "' " +
                "                 Foreground='" + ColOf(t.TextPrimary) + "' " +
                "                 TextTrimming='CharacterEllipsis'/>" +
                "      <TextBlock Text='{Binding ListSubtitle}' " +
                "                 FontSize='" + N(t.CaptionSize) + "' " +
                "                 Foreground='" + ColOf(t.TextMuted) + "' " +
                "                 TextTrimming='CharacterEllipsis' Margin='0,1,0,0'/>" +
                "    </StackPanel>" +

                "    <Path Grid.Column='2' Data='" + Icons.Get("star").ToString() + "' " +
                "          Stroke='" + ColOf(t.Accent) + "' StrokeThickness='1.4' " +
                "          Fill='Transparent' Width='15' Height='15' " +
                "          Stretch='Uniform' VerticalAlignment='Center' Margin='0,0,12,0'>" +
                "      <Path.Style>" +
                "        <Style TargetType='{x:Type Path}'>" +
                "          <Style.Triggers>" +
                "            <DataTrigger Binding='{Binding Favorite}' Value='True'>" +
                "              <Setter Property='Fill' Value='" + ColOf(t.Accent) + "'/>" +
                "            </DataTrigger>" +
                "            <DataTrigger Binding='{Binding Favorite}' Value='False'>" +
                "              <Setter Property='Visibility' Value='Collapsed'/>" +
                "            </DataTrigger>" +
                "          </Style.Triggers>" +
                "        </Style>" +
                "      </Path.Style>" +
                "    </Path>" +
                "  </Grid>" +
                "</DataTemplate>";

            return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private StackPanel BuildEmptyState(Theme t)
        {
            StackPanel stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24)
            };

            Border mark = Factory.Rect(52, 52, 14, t.Input, 0);
            Grid markHost = new Grid { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Center };
            markHost.Children.Add(mark);
            markHost.Children.Add(Factory.Icon("search", 22, t.TextMuted));
            stack.Children.Add(markHost);

            TextBlock title = Factory.Text("Nothing here", t.HeadingSize, t.TextPrimary, true);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, 16, 0, 0);
            stack.Children.Add(title);

            TextBlock body = Factory.Body("No items match this view. Press Ctrl+N to add a login.",
                t.TextMuted);
            body.HorizontalAlignment = HorizontalAlignment.Center;
            body.TextAlignment = TextAlignment.Center;
            body.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(body);

            return stack;
        }

        // =====================================================================================
        // Detail pane
        // =====================================================================================

        private void BuildDetail()
        {
            if (_detailHost == null) return;

            Theme t = Factory.Theme;

            if (_selected == null)
            {
                StackPanel stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Border mark = Factory.Rect(56, 56, 15, t.Input, 0);
                Grid host = new Grid { Width = 56, Height = 56, HorizontalAlignment = HorizontalAlignment.Center };
                host.Children.Add(mark);
                host.Children.Add(Factory.Icon("vault", 26, t.TextMuted));
                stack.Children.Add(host);
                TextBlock hint = Factory.Text("Select an item to see its details", t.BodySize, t.TextMuted);
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                hint.Margin = new Thickness(0, 18, 0, 0);
                stack.Children.Add(hint);
                _detailHost.Content = stack;
                return;
            }

            _detailHost.Content = BuildDetailContent(_selected);
        }

        private UIElement BuildDetailContent(VaultItem item)
        {
            Theme t = Factory.Theme;

            Grid root = new Grid { Background = t.Background };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ---- header -------------------------------------------------------------------
            Border header = new Border
            {
                Background = t.Sidebar,
                Padding = new Thickness(28, 20, 28, 20),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = t.Border
            };

            Grid headerGrid = new Grid();
            StackPanel titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            StackPanel nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock name = Factory.Text(item.Name, t.TitleSize, t.TextPrimary);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextWrapping = TextWrapping.Wrap;
            nameRow.Children.Add(name);

            if (item.Favorite)
            {
                Path star = Factory.IconFilled("star", 16, t.Accent);
                star.Margin = new Thickness(10, 0, 0, 0);
                star.VerticalAlignment = VerticalAlignment.Center;
                nameRow.Children.Add(star);
            }
            titleStack.Children.Add(nameRow);

            TextBlock meta = Factory.Text(BuildMetaLine(item), t.CaptionSize, t.TextMuted);
            meta.Margin = new Thickness(0, 4, 0, 0);
            titleStack.Children.Add(meta);

            headerGrid.Children.Add(titleStack);

            StackPanel headerActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerActions.Children.Add(Factory.Button(null, ButtonKind.Ghost,
                delegate { _session.ToggleFavorite(item); BuildDetail(); }, "star", 32));
            headerActions.Children.Add(Factory.Button("Delete", ButtonKind.Danger,
                delegate { DeleteItem(item); }, "trash", 34));
            headerGrid.Children.Add(headerActions);

            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ---- body ---------------------------------------------------------------------
            ScrollViewer scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroller, 1);

            StackPanel body = new StackPanel { Margin = new Thickness(28, 24, 28, 32), MaxWidth = 620 };
            body.HorizontalAlignment = HorizontalAlignment.Left;

            if (item is Credential)
                BuildCredentialBody(body, (Credential)item);
            else
                BuildNoteBody(body, (SecureNote)item);

            scroller.Content = body;
            root.Children.Add(scroller);
            return root;
        }

        private string BuildMetaLine(VaultItem item)
        {
            string kind = item.Kind == ItemKind.SecureNote ? "Secure note" : "Login";
            string modified = "Edited " + Relative(item.ModifiedUtc);
            return kind + "  \u00b7  " + modified;
        }

        private static string Relative(DateTime utc)
        {
            TimeSpan delta = DateTime.UtcNow - utc.ToLocalTime();
            if (delta.TotalMinutes < 1) return "just now";
            if (delta.TotalMinutes < 60) return ((int)delta.TotalMinutes) + " min ago";
            if (delta.TotalHours < 24) return ((int)delta.TotalHours) + " h ago";
            if (delta.TotalDays < 30) return ((int)delta.TotalDays) + " d ago";
            return utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        }

        private void BuildCredentialBody(StackPanel body, Credential c)
        {
            Theme t = Factory.Theme;

            body.Children.Add(Field("Username", c.Username, false, delegate
            {
                CopySecret(c.Username, "Username copied");
            }));

            body.Children.Add(Spacer(10));

            body.Children.Add(SecretField("Password", c.Password, delegate
            {
                CopySecret(c.Password, "Password copied");
            }));

            if (!string.IsNullOrEmpty(c.Password))
            {
                PasswordStrength strength = PasswordGenerator.Estimate(c.Password);
                body.Children.Add(StrengthBar(strength));

                int reuse = _session.CountPasswordReuse(c);
                if (reuse > 0)
                {
                    body.Children.Add(Spacer(8));
                    body.Children.Add(InlineWarning("warning", t.Warning,
                        "This password is also used by " + reuse + " other " +
                        (reuse == 1 ? "entry" : "entries") + ". Reusing a password means one leak exposes all of them."));
                }
                else if (strength.Score <= 1)
                {
                    body.Children.Add(Spacer(8));
                    body.Children.Add(InlineWarning("warning", t.Warning, strength.Advice));
                }
            }

            if (!string.IsNullOrEmpty(c.Url))
            {
                body.Children.Add(Spacer(18));
                body.Children.Add(Field("Website", c.Url, false, delegate
                {
                    CopySecret(c.Url, "Address copied");
                }, "globe"));
            }

            if (!string.IsNullOrEmpty(c.Notes))
            {
                body.Children.Add(Spacer(18));
                body.Children.Add(SectionLabel("Notes"));
                body.Children.Add(Spacer(8));
                Border notes = Factory.Card();
                notes.Padding = new Thickness(14);
                notes.HorizontalAlignment = HorizontalAlignment.Left;
                notes.MinWidth = 320;
                notes.Child = Factory.Body(c.Notes, t.TextSecondary);
                body.Children.Add(notes);
            }

            if (c.Tags.Count > 0)
            {
                body.Children.Add(Spacer(18));
                body.Children.Add(SectionLabel("Tags"));
                body.Children.Add(Spacer(8));
                WrapPanel tags = new WrapPanel();
                foreach (string tag in c.Tags) tags.Children.Add(Factory.Pill(tag, t.TextSecondary, t.Input));
                body.Children.Add(tags);
            }

            body.Children.Add(Spacer(24));
            body.Children.Add(EditButton(c));
        }

        private void BuildNoteBody(StackPanel body, SecureNote n)
        {
            Theme t = Factory.Theme;

            Border card = Factory.Card();
            card.Padding = new Thickness(16);
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.MinWidth = 380;
            card.MaxWidth = 640;
            card.Child = Factory.Body(n.Body, t.TextSecondary);
            body.Children.Add(card);

            if (n.Tags.Count > 0)
            {
                body.Children.Add(Spacer(18));
                body.Children.Add(SectionLabel("Tags"));
                body.Children.Add(Spacer(8));
                WrapPanel tags = new WrapPanel();
                foreach (string tag in n.Tags) tags.Children.Add(Factory.Pill(tag, t.TextSecondary, t.Input));
                body.Children.Add(tags);
            }

            body.Children.Add(Spacer(24));
            body.Children.Add(EditButton(n));
        }

        private TextBlock SectionLabel(string text)
        {
            return Factory.Label(text, Factory.Theme.TextMuted);
        }

        private UIElement EditButton(VaultItem item)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Factory.Button("Edit", ButtonKind.Secondary, delegate { EditItem(item); }, "note", 36));
            return row;
        }

        /// <summary>A read-only field row with a copy action.</summary>
        private UIElement Field(string label, string value, bool mono, Action onCopy, string icon = null)
        {
            Theme t = Factory.Theme;
            StackPanel stack = new StackPanel();

            if (!string.IsNullOrEmpty(label)) stack.Children.Add(Factory.Label(label, t.TextMuted));
            stack.Children.Add(Spacer(7));

            Border card = Factory.Card();
            card.MinWidth = 320;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.Padding = new Thickness(12, 0, 6, 0);

            Grid row = new Grid { Height = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock text = Factory.Text(value, mono ? t.MonoSize : t.BodySize, t.TextPrimary);
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(text, 0);
            row.Children.Add(text);

            Button copy = Factory.Button(null, ButtonKind.Ghost, onCopy, "copy", 30);
            copy.Width = 30;
            Grid.SetColumn(copy, 2);
            row.Children.Add(copy);

            card.Child = row;
            stack.Children.Add(card);
            return stack;
        }

        /// <summary>A masked field with reveal and copy controls.</summary>
        private UIElement SecretField(string label, string value, Action onCopy)
        {
            Theme t = Factory.Theme;
            StackPanel stack = new StackPanel();
            stack.Children.Add(Factory.Label(label, t.TextMuted));
            stack.Children.Add(Spacer(7));

            Border card = Factory.Card();
            card.MinWidth = 320;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.Padding = new Thickness(12, 0, 6, 0);

            Grid row = new Grid { Height = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock display = Factory.Text(_revealSelected ? value : Mask(value), t.MonoSize, t.TextPrimary);
            display.FontFamily = t.MonoFamily;
            display.VerticalAlignment = VerticalAlignment.Center;
            display.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(display);

            Button reveal = Factory.Button(null, ButtonKind.Ghost, delegate
            {
                _revealSelected = !_revealSelected;
                BuildDetail();
            }, _revealSelected ? "eye-off" : "eye", 30);
            reveal.Width = 30;
            reveal.ToolTip = _revealSelected ? "Hide password" : "Reveal password";
            Grid.SetColumn(reveal, 1);
            row.Children.Add(reveal);

            Button copy = Factory.Button(null, ButtonKind.Ghost, onCopy, "copy", 30);
            copy.Width = 30;
            copy.ToolTip = "Copy password";
            Grid.SetColumn(copy, 2);
            row.Children.Add(copy);

            card.Child = row;
            stack.Children.Add(card);
            return stack;
        }

        /// <summary>Replaces each character with a bullet so length is not leaked either.</summary>
        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return new string('\u2022', value.Length);
        }

        private UIElement StrengthBar(PasswordStrength strength)
        {
            Theme t = Factory.Theme;
            Brush colour;
            switch (strength.Score)
            {
                case 0:
                case 1: colour = t.Danger; break;
                case 2: colour = t.Warning; break;
                default: colour = t.Success; break;
            }

            Grid grid = new Grid { Margin = new Thickness(0, 10, 0, 0), MinWidth = 320 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid track = new Grid { Width = 120, Height = 5, VerticalAlignment = VerticalAlignment.Center };
            Border bg = Factory.Rect(120, 5, 3, t.Border);
            track.Children.Add(bg);
            double fraction = Math.Max(0.08, Math.Min(1.0, strength.Bits / 110.0));
            Border fill = Factory.Rect(120 * fraction, 5, 3, colour);
            track.Children.Add(fill);
            Grid.SetColumn(track, 0);
            grid.Children.Add(track);

            TextBlock label = Factory.Text(strength.Label + " \u00b7 about " + strength.Bits + " bits",
                t.CaptionSize, colour);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            return grid;
        }

        private UIElement InlineWarning(string icon, Brush colour, string message)
        {
            Theme t = Factory.Theme;
            Border border = new Border
            {
                Background = t.WarningSoft,
                BorderBrush = colour,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(t.RadiusControl),
                Padding = new Thickness(12, 10, 12, 10),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 320
            };
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Factory.Icon(icon, 17, colour));
            TextBlock text = Factory.Body(message, t.TextSecondary);
            text.Margin = new Thickness(10, 0, 0, 0);
            text.MaxWidth = 460;
            row.Children.Add(text);
            border.Child = row;
            return border;
        }

        // =====================================================================================
        // Editing
        // =====================================================================================

        private void EditItem(VaultItem item)
        {
            _detailHost.Content = BuildEditor(item);
        }

        private UIElement BuildEditor(VaultItem item)
        {
            Theme t = Factory.Theme;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border header = new Border
            {
                Background = t.Sidebar,
                Padding = new Thickness(28, 20, 28, 20),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = t.Border
            };
            Grid headerGrid = new Grid();
            TextBlock title = Factory.Text(item.Kind == ItemKind.SecureNote ? "Edit note" : "Edit login",
                t.TitleSize, t.TextPrimary);
            title.VerticalAlignment = VerticalAlignment.Center;
            headerGrid.Children.Add(title);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            actions.Children.Add(Factory.Button("Cancel", ButtonKind.Ghost, delegate { BuildDetail(); }));
            actions.Children.Add(Factory.Button("Save", ButtonKind.Primary, delegate { Save(item); }));
            headerGrid.Children.Add(actions);
            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            ScrollViewer scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroller, 1);

            StackPanel body = new StackPanel { Margin = new Thickness(28, 24, 28, 32), MaxWidth = 640 };

            TextBox nameBox = Factory.TextInput(40);
            nameBox.Text = item.Name;
            AutomationProperties.SetName(nameBox, "Name");
            body.Children.Add(EditorField("Name", nameBox));

            Credential c = item as Credential;
            if (c != null)
            {
                body.Children.Add(Spacer(14));

                TextBox userBox = Factory.TextInput(40);
                userBox.Text = c.Username;
                AutomationProperties.SetName(userBox, "Username");
                body.Children.Add(EditorField("Username", userBox));

                body.Children.Add(Spacer(14));

                Grid passwordRow = new Grid();
                passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Factory.SecretInput passInput = Factory.SecretField(40, true);
                passInput.SetValue(c.Password);
                AutomationProperties.SetName(passInput.Element, "Password");
                Grid.SetColumn(passInput.Element, 0);
                passwordRow.Children.Add(passInput.Element);

                Button generate = Factory.Button(null, ButtonKind.Ghost, delegate
                {
                    passInput.SetValue(Core.Crypto.PasswordGenerator.Generate(
                        new Core.Crypto.GeneratorOptions { Length = 20 }));
                }, "wand", 40);
                generate.Width = 42;
                generate.ToolTip = "Generate a strong password";
                generate.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(generate, 1);
                passwordRow.Children.Add(generate);

                _editPassword = passInput;

                body.Children.Add(EditorField("Password", passwordRow));

                body.Children.Add(Spacer(14));

                TextBox urlBox = Factory.TextInput(40);
                urlBox.Text = c.Url;
                AutomationProperties.SetName(urlBox, "Website");
                body.Children.Add(EditorField("Website", urlBox));

                body.Children.Add(Spacer(14));

                TextBox notesBox = Factory.TextInput(110, true);
                notesBox.Text = c.Notes;
                AutomationProperties.SetName(notesBox, "Notes");
                body.Children.Add(EditorField("Notes", notesBox));

                _editName = nameBox; _editUser = userBox;
                _editUrl = urlBox; _editNotes = notesBox; _editBody = null;
            }
            else
            {
                SecureNote n = (SecureNote)item;
                body.Children.Add(Spacer(14));

                TextBox noteBody = Factory.TextInput(220, true);
                noteBody.Text = n.Body;
                AutomationProperties.SetName(noteBody, "Note body");
                body.Children.Add(EditorField("Note", noteBody));

                _editName = nameBox; _editBody = noteBody;
                _editUser = null; _editPassword = null; _editUrl = null; _editNotes = null;
            }

            scroller.Content = body;
            root.Children.Add(scroller);
            return root;
        }

        private TextBox _editName, _editUser, _editUrl, _editNotes, _editBody;
        private Factory.SecretInput _editPassword;

        private UIElement EditorField(string label, UIElement control)
        {
            Theme t = Factory.Theme;
            StackPanel stack = new StackPanel();
            stack.Children.Add(Factory.Label(label, t.TextMuted));
            stack.Children.Add(Spacer(7));

            FrameworkElement element = control as FrameworkElement;
            if (element != null)
            {
                element.HorizontalAlignment = HorizontalAlignment.Left;
                element.MinWidth = 360;
            }

            stack.Children.Add(control);
            return stack;
        }

        private void Save(VaultItem item)
        {
            if (_editName == null) return;

            item.Name = string.IsNullOrWhiteSpace(_editName.Text) ? "Untitled" : _editName.Text.Trim();

            Credential c = item as Credential;
            if (c != null && _editPassword != null)
            {
                c.Username = _editUser.Text;
                c.Password = _editPassword == null ? string.Empty : _editPassword.GetValue();
                c.Url = _editUrl.Text;
                c.Notes = _editNotes.Text;
            }
            else if (_editBody != null)
            {
                ((SecureNote)item).Body = _editBody.Text;
            }

            try
            {
                _session.Commit(item);
                _revealSelected = false;
                RefreshList();
                BuildDetail();
                _window.Notifications.Show("Saved.");
            }
            catch (Exception ex)
            {
                _window.Notifications.Show("Could not save: " + ex.Message, true);
            }
        }

        private void DeleteItem(VaultItem item)
        {
            if (!_window.AppSettings.ConfirmBeforeDelete)
            {
                PerformDelete(item);
                return;
            }

            bool confirmed = Dialogs.Confirm(_window, "Delete this item?",
                "\"" + item.Name + "\" will be permanently removed from your vault. This cannot be undone.",
                "Delete", true);

            if (confirmed) PerformDelete(item);
        }

        private void PerformDelete(VaultItem item)
        {
            try
            {
                _session.Delete(item);
                _selected = null;
                _revealSelected = false;
                RefreshList();
                BuildDetail();
                _window.Notifications.Show("Deleted \"" + item.Name + "\".");
            }
            catch (Exception ex)
            {
                _window.Notifications.Show("Could not delete: " + ex.Message, true);
            }
        }

        private void CopySecret(string value, string message)
        {
            if (string.IsNullOrEmpty(value))
            {
                _window.Notifications.Show("There is nothing to copy.");
                return;
            }

            if (_window.Clipboard.Copy(value, _window.AppSettings.ClearClipboard
                    ? _window.AppSettings.ClipboardClearSeconds : 0))
            {
                _window.Clipboard.Remember(value);
            }
        }

        private void Select(VaultItem item)
        {
            _selected = item;
            _revealSelected = false;
            BuildDetail();
        }

        // =====================================================================================
        // Responsive behaviour
        // =====================================================================================

        private void ApplyResponsiveLayout()
        {
            if (_window == null || _listColumn == null || _detailColumn == null) return;

            bool narrow = _window.ActualWidth > 0 && _window.ActualWidth < Factory.Theme.NarrowBreakpoint;
            bool showingDetail = _selected != null;

            // Below the breakpoint the detail pane replaces the list, reached with the back action.
            // Three columns on a narrow window would leave none of them usable.
            _detailColumn.Visibility = (narrow && showingDetail) ? Visibility.Visible : Visibility.Visible;
            _listColumn.Visibility = (narrow && showingDetail) ? Visibility.Collapsed : Visibility.Visible;

            if (narrow && showingDetail)
            {
                BackToList();
            }
        }

        /// <summary>In narrow mode, returns from the detail pane to the list.</summary>
        private void BackToList()
        {
            if (_listColumn == null) return;
            if (_listColumn.Visibility != Visibility.Collapsed) return;

            _selected = null;
            _revealSelected = false;
            BuildDetail();
            ApplyResponsiveLayout();
            FocusSearch();
        }
    }
}
