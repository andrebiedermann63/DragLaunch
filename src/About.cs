using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragLaunch
{
    /// <summary>Programmversion: nur ganze Zahlen, angezeigt als 01, 02, 03 ...</summary>
    public static class AppInfo
    {
        public const int Version = 24;
        public static string VersionText => L.F("about.version", Version.ToString("00"));
    }

    /// <summary>Vergrössert Dialogfenster um den eingestellten Faktor (Einstellung "Grösse der Dialogfenster").</summary>
    public static class UiScale
    {
        public static double Factor => Math.Max(0.8, Math.Min(3.0, ConfigStore.Settings.DialogScale));

        /// <summary>Nach dem Setzen von Content aufrufen. Kann erneut aufgerufen werden, wenn sich der Faktor ändert.</summary>
        public static void Apply(Window w)
        {
            if (!(w.Content is FrameworkElement fe)) return;
            double f = Factor;
            double old = w.Resources.Contains("UiScale") ? (double)w.Resources["UiScale"] : 1.0;
            if (Math.Abs(f - old) < 0.001 && w.Resources.Contains("UiScale")) return;

            fe.LayoutTransform = Math.Abs(f - 1.0) < 0.001 ? Transform.Identity : new ScaleTransform(f, f);
            double k = f / old;
            Rect area = SystemParameters.WorkArea;
            if (!double.IsNaN(w.Width)) w.Width = Math.Min(w.Width * k, area.Width * 0.95);
            if (!double.IsNaN(w.Height)) w.Height = Math.Min(w.Height * k, area.Height * 0.95);
            if (w.MinWidth > 0) w.MinWidth = Math.Min(w.MinWidth * k, area.Width * 0.9);
            w.Resources["UiScale"] = f;
        }
    }

    public class AboutWindow : Window
    {
        public AboutWindow()
        {
            Title = L.T("about.title");
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            var logo = new Image { Width = 96, Height = 96, Margin = new Thickness(0, 4, 0, 10) };
            try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/DragLaunch256.png")); } catch { }
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);

            string built = "";
            try { built = File.GetLastWriteTime(Environment.ProcessPath).ToString("dd.MM.yyyy HH:mm"); } catch { }

            var sp = new StackPanel { Margin = new Thickness(20) };
            sp.Children.Add(logo);
            sp.Children.Add(Center(new TextBlock { Text = "DragLaunch", FontSize = 24, FontWeight = FontWeights.SemiBold }));
            sp.Children.Add(Center(new TextBlock { Text = AppInfo.VersionText, FontSize = 16, Margin = new Thickness(0, 2, 0, 0) }));
            if (built.Length > 0)
                sp.Children.Add(Center(new TextBlock { Text = L.F("about.built", built), Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) }));
            sp.Children.Add(Center(new TextBlock
            {
                Text = L.T("about.desc"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 14, 0, 14)
            }));
            sp.Children.Add(new TextBlock { Text = L.T("about.appfolder"), Foreground = Brushes.Gray });
            sp.Children.Add(new TextBox { Text = ConfigStore.AppDir, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent });
            sp.Children.Add(new TextBlock { Text = L.T("about.settings"), Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) });
            sp.Children.Add(new TextBox { Text = ConfigStore.IniPath, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent });

            var bFolder = new Button { Content = L.T("main.appfolder"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            bFolder.Click += (s, e) => Launcher.Open(ConfigStore.AppDir);
            var bOk = new Button { Content = L.T("ok"), Width = 90, IsDefault = true, IsCancel = true };
            bOk.Click += (s, e) => Close();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            buttons.Children.Add(bFolder);
            buttons.Children.Add(bOk);
            sp.Children.Add(buttons);

            Content = sp;
            UiScale.Apply(this);
        }

        private static FrameworkElement Center(FrameworkElement e)
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            return e;
        }
    }
}
