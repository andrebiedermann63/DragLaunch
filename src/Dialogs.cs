using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DragLaunch
{
    // =========================================================================
    //  Autostart über Verknüpfung im Autostart-Ordner
    // =========================================================================
    public static class Autostart
    {
        private static string LnkPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DragLaunch.lnk");

        public static bool Exists => File.Exists(LnkPath);

        public static void Create()
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic sc = shell.CreateShortcut(LnkPath);
            sc.TargetPath = Environment.ProcessPath;
            sc.WorkingDirectory = ConfigStore.AppDir;
            sc.Description = "DragLaunch";
            sc.Save();
        }

        public static void Remove()
        {
            if (File.Exists(LnkPath)) File.Delete(LnkPath);
        }
    }

    // =========================================================================
    //  Einfache Texteingabe
    // =========================================================================
    public static class InputBox
    {
        public static string Show(Window owner, string title, string prompt, string value)
        {
            var w = new Window
            {
                Title = title,
                Owner = owner,
                SizeToContent = SizeToContent.Height,
                Width = 360,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13
            };
            var tb = new TextBox { Text = value ?? "", Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(2) };
            var ok = new Button { Content = L.T("ok"), Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("cancel"), Width = 80, IsCancel = true };
            ok.Click += (s, e) => w.DialogResult = true;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = prompt });
            sp.Children.Add(tb);
            sp.Children.Add(buttons);
            w.Content = sp;
            w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            UiScale.Apply(w);
            return w.ShowDialog() == true ? tb.Text : null;
        }
    }

    // =========================================================================
    //  Eigenschaften eines Icons
    // =========================================================================
    public class ItemPropertiesWindow : Window
    {
        private readonly ItemData _it;
        private readonly TextBox _label, _path, _args, _workDir, _icon;
        private readonly CheckBox _admin;
        private readonly ComboBox _folderMode;
        private readonly Grid _grid = new Grid();

        public ItemPropertiesWindow(ItemData it)
        {
            _it = it;
            Title = L.F("props.title", it.Label);
            Width = 600;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _grid.Margin = new Thickness(14);
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _label = Row(L.T("props.label"), it.Label);
            _path = Row(L.T("props.path"), it.Path,
                Btn(L.T("props.file"), () => PickFile(_path, L.T("filter.all"))),
                Btn(L.T("props.folder"), () => PickFolder(_path)));
            Hint(L.T("props.pathhint"));
            _args = Row(L.T("props.args"), it.Arguments);
            _workDir = Row(L.T("props.workdir"), it.WorkDir, Btn("…", () => PickFolder(_workDir)));
            _icon = Row(L.T("props.icon"), it.IconFile,
                Btn("…", () => PickFile(_icon, L.T("filter.icons"), IconStore.Folder)));
            Hint(L.T("props.iconhint"));

            _admin = new CheckBox { Content = L.T("item.runadmin"), IsChecked = it.RunAsAdmin };
            Place(null, _admin);

            _folderMode = new ComboBox
            {
                ItemsSource = new[] { L.T("props.fmdefault"), L.T("props.fmlist"), L.T("folder.open") },
                SelectedIndex = (int)it.FolderMode,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 260
            };
            Place(L.T("props.folderclick"), _folderMode);

            var ok = new Button { Content = L.T("ok"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("cancel"), Width = 90, IsCancel = true };
            ok.Click += (s, e) => Commit();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, _grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(buttons, 3);
            _grid.Children.Add(buttons);

            Content = _grid;
            UiScale.Apply(this);
            Loaded += (s, e) => { _label.Focus(); _label.SelectAll(); };
        }

        private void Commit()
        {
            if (string.IsNullOrWhiteSpace(_path.Text))
            {
                MessageBox.Show(this, L.T("props.needpath"), "DragLaunch");
                return;
            }
            _it.Label = _label.Text.Trim();
            _it.Path = _path.Text.Trim().Trim('"');
            _it.Arguments = _args.Text.Trim();
            _it.WorkDir = _workDir.Text.Trim().Trim('"');
            string icon = _icon.Text.Trim().Trim('"');
            if (icon.Length > 0)
            {
                try { icon = IconStore.Collect(icon); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.F("props.iconfail", ex.Message),
                        "DragLaunch", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            _it.IconFile = icon;
            _it.RunAsAdmin = _admin.IsChecked == true;
            _it.FolderMode = (FolderMode)Math.Max(0, _folderMode.SelectedIndex);
            DialogResult = true;
        }

        private TextBox Row(string label, string value, params Button[] buttons)
        {
            var tb = new TextBox { Text = value ?? "", VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2) };
            var bp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (Button b in buttons) bp.Children.Add(b);
            Place(label, tb, bp);
            return tb;
        }

        private void Place(string label, FrameworkElement control, FrameworkElement extra = null)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int r = _grid.RowDefinitions.Count - 1;
            if (label != null)
            {
                var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
                Grid.SetRow(tb, r);
                _grid.Children.Add(tb);
            }
            control.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            _grid.Children.Add(control);
            if (extra != null)
            {
                extra.Margin = new Thickness(6, 4, 0, 4);
                Grid.SetRow(extra, r);
                Grid.SetColumn(extra, 2);
                _grid.Children.Add(extra);
            }
        }

        private void Hint(string text)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = text, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetRow(tb, _grid.RowDefinitions.Count - 1);
            Grid.SetColumn(tb, 1);
            _grid.Children.Add(tb);
        }

        private static Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
            b.Click += (s, e) => a();
            return b;
        }

        private void PickFile(TextBox target, string filter, string defaultDir = null)
        {
            var d = new OpenFileDialog { Filter = filter, DereferenceLinks = false, CheckFileExists = true };
            try
            {
                string cur = ReferenceEquals(target, _icon) ? IconStore.Resolve(target.Text) : Environment.ExpandEnvironmentVariables(target.Text.Trim());
                if (File.Exists(cur)) d.InitialDirectory = Path.GetDirectoryName(cur);
                else if (defaultDir != null && Directory.Exists(defaultDir)) d.InitialDirectory = defaultDir;
            }
            catch { }
            if (d.ShowDialog(this) == true) target.Text = d.FileName;
        }

        private void PickFolder(TextBox target)
        {
            var d = new OpenFolderDialog();
            try
            {
                string cur = Environment.ExpandEnvironmentVariables(target.Text.Trim());
                if (Directory.Exists(cur)) d.InitialDirectory = cur;
            }
            catch { }
            if (d.ShowDialog(this) == true) target.Text = d.FolderName;
        }
    }

    // =========================================================================
    //  Backups verwalten
    // =========================================================================
    public class BackupWindow : Window
    {
        private readonly MainWindow _main;
        private readonly ListBox _list = new ListBox { MinHeight = 220 };

        private class Entry
        {
            public string Path;
            public override string ToString()
            {
                string name = System.IO.Path.GetFileName(Path);
                try { return name + "     (" + File.GetLastWriteTime(Path).ToString("dd.MM.yyyy HH:mm") + ")"; }
                catch { return name; }
            }
        }

        public BackupWindow(MainWindow main)
        {
            _main = main;
            Title = L.T("bw.title");
            Width = 480;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var bNew = Btn(L.T("bw.new"), NewBackup);
            var bRestore = Btn(L.T("bw.restore"), Restore);
            var bDelete = Btn(L.T("bw.delete"), Delete);
            var bClose = Btn(L.T("close"), Close);
            bClose.IsCancel = true;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(bNew);
            buttons.Children.Add(bRestore);
            buttons.Children.Add(bDelete);
            buttons.Children.Add(bClose);

            var info = new TextBlock
            {
                Text = L.T("bw.info"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var root = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(info, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(info);
            root.Children.Add(buttons);
            root.Children.Add(_list);
            Content = root;
            UiScale.Apply(this);

            _list.MouseDoubleClick += (s, e) => Restore();
            Fill();
        }

        private void Fill()
        {
            _list.Items.Clear();
            foreach (string p in ConfigStore.ListBackups()) _list.Items.Add(new Entry { Path = p });
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void NewBackup()
        {
            try { ConfigStore.CreateBackup(); Fill(); }
            catch (Exception ex) { MessageBox.Show(this, L.F("backup.failed", ex.Message), "DragLaunch"); }
        }

        private void Restore()
        {
            if (!(_list.SelectedItem is Entry e)) return;
            var r = MessageBox.Show(this,
                L.F("bw.restoreq", Path.GetFileName(e.Path)),
                L.T("bw.restoretitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            try
            {
                if (r == MessageBoxResult.Yes) ConfigStore.CreateBackup();
                ConfigStore.RestoreBackup(e.Path);
                _main.ReloadAll();
                MessageBox.Show(this, L.T("bw.restored"), "DragLaunch");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("bw.restorefail", ex.Message), "DragLaunch");
            }
        }

        private void Delete()
        {
            if (!(_list.SelectedItem is Entry e)) return;
            if (MessageBox.Show(this, L.F("bw.deleteq", Path.GetFileName(e.Path)),
                    "DragLaunch", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { File.Delete(e.Path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "DragLaunch"); }
            Fill();
        }

        private static Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (s, e) => a();
            return b;
        }
    }
}
