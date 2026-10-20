//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Telegram.Common;
using Telegram.Navigation;
using Telegram.Services;
using Telegram.Views.Settings;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace Telegram.Host
{
    /// <summary>
    /// End-to-end test driver for the <c>--no-activate</c> mode. The app is steered by the lines
    /// of <c>LocalState\e2e\cmd.txt</c> rather than by UI Automation: an automation call grants
    /// the provider the right to take the foreground, and the focus change after a navigation
    /// then uses it. Commands run on the main window's thread; results go to <c>out.txt</c>.
    ///
    /// A watchdog backs it up: should any window of this process still end up in the foreground,
    /// the foreground goes straight back to whichever window held it before.
    /// </summary>
    internal static partial class E2eDriver
    {
        private static string _folder;
        private static long _consumed;
        private static IntPtr _restore;
        private static readonly uint _pid = (uint)Environment.ProcessId;

        public static void Start()
        {
            if (!IslandWindow.NoActivate)
            {
                return;
            }

            _restore = Win32.GetForegroundWindow();

            new Thread(Watchdog) { IsBackground = true, Name = "e2e-watchdog" }.Start();

            _folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "e2e");
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "cmd.txt"), string.Empty);
            File.WriteAllText(Path.Combine(_folder, "out.txt"), string.Empty);

            new Thread(Poll) { IsBackground = true, Name = "e2e-commands" }.Start();
        }

        private static void Watchdog()
        {
            while (true)
            {
                var foreground = Win32.GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(foreground, out var pid);

                    if (pid == _pid)
                    {
                        if (_restore != IntPtr.Zero)
                        {
                            Win32.SetForegroundWindow(_restore);
                        }
                    }
                    else
                    {
                        _restore = foreground;
                    }
                }

                Thread.Sleep(15);
            }
        }

        private static void Poll()
        {
            var path = Path.Combine(_folder, "cmd.txt");

            while (true)
            {
                Thread.Sleep(200);

                try
                {
                    var text = File.ReadAllText(path);
                    if (text.Length <= _consumed)
                    {
                        continue;
                    }

                    var lines = text.Substring((int)_consumed).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    _consumed = text.Length;

                    foreach (var line in lines)
                    {
                        var window = WindowContext.Main;
                        if (window == null)
                        {
                            Write(line, "error: no main window");
                            continue;
                        }

                        window.Dispatcher.Dispatch(() =>
                        {
                            try
                            {
                                Write(line, Execute(window, line));
                            }
                            catch (Exception ex)
                            {
                                Write(line, "error: " + ex.GetType().Name + ": " + ex.Message);
                            }
                        });
                    }
                }
                catch (IOException)
                {
                    // The test is still writing the file; the next tick picks it up.
                }
            }
        }

        private static void Write(string command, string result)
        {
            lock (_folder)
            {
                File.AppendAllText(Path.Combine(_folder, "out.txt"), command + " => " + result + "\n");
            }
        }

        private static string Execute(WindowContext window, string line)
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var navigation = window.GetNavigationService();
            var root = window.Content as DependencyObject;

            switch (parts[0])
            {
                case "nav":
                    var page = parts[1] switch
                    {
                        "appearance" => typeof(SettingsAppearancePage),
                        "advanced" => typeof(SettingsAdvancedPage),
                        "privacy" => typeof(SettingsPrivacyAndSecurityPage),
                        "notifications" => typeof(SettingsNotificationsPage),
                        "data" => typeof(SettingsDataAndStoragePage),
                        "language" => typeof(SettingsLanguagePage),
                        "themes" => typeof(SettingsThemesPage),
                        "backgrounds" => typeof(SettingsBackgroundsPage),
                        "nightmode" => typeof(SettingsNightModePage),
                        _ => throw new ArgumentException("unknown page " + parts[1])
                    };

                    return navigation.Navigate(page) ? "ok" : "refused";

                case "gc":
                    {
                        // Separates a real handle leak from handles that are only waiting for
                        // their wrappers to be finalized: count before and after this.
                        var before = GC.GetTotalMemory(false);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        return $"ok {before / 1048576}MB -> {GC.GetTotalMemory(true) / 1048576}MB";
                    }

                case "navtype":
                    {
                        // Any page by type name, for walking every page in a smoke test.
                        var type = typeof(SettingsAppearancePage).Assembly.GetType(parts[1], throwOnError: true);
                        var ok = navigation.Navigate(type);
                        return (ok ? "ok " : "refused ") + (navigation.Content?.GetType().Name ?? "null");
                    }

                case "chat":
                    if (navigation is not TLNavigationService service)
                    {
                        return "error: no chat navigation";
                    }

                    var chatId = parts[1] == "saved" ? service.ClientService.Options.MyId : long.Parse(parts[1]);
                    service.NavigateToChat(chatId);
                    return "ok " + chatId;

                case "back":
                    navigation.GoBack();
                    return "ok";

                case "slider":
                    {
                        var slider = Find<Slider>(root, parts[2]);
                        slider.Value = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                        return "ok " + slider.Value;
                    }

                case "combo":
                    {
                        var combo = Find<ComboBox>(root, parts[2]);
                        combo.SelectedIndex = int.Parse(parts[1]);
                        return "ok " + combo.SelectedIndex;
                    }

                case "check":
                    {
                        var toggle = Find<ToggleButton>(root, parts[2]);
                        toggle.IsChecked = parts[1] == "1";
                        return "ok " + toggle.IsChecked;
                    }

                case "show":
                    {
                        // Brings a named element into view: the transparency block sits below the
                        // fold of the appearance page.
                        var element = Find<FrameworkElement>(root, parts.Length > 2 ? parts[1] + " " + parts[2] : parts[1]);
                        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.1 });
                        return "ok";
                    }

                case "probe":
                    {
                        // Every element under a point, top first, that paints a background: the
                        // way to find which layer keeps an area from being see-through. The point
                        // is in physical pixels of the window, as a screenshot measures it.
                        var scale = window.Content.XamlRoot.RasterizationScale;
                        var point = new Windows.Foundation.Point(double.Parse(parts[1]) / scale, double.Parse(parts[2]) / scale);
                        var layers = new List<string>();

                        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(point, window.Content, true))
                        {
                            var brush = element switch
                            {
                                Panel panel => panel.Background,
                                Border border => border.Background,
                                Control control => control.Background,
                                ContentPresenter presenter => presenter.Background,
                                Windows.UI.Xaml.Shapes.Shape shape => shape.Fill,
                                _ => null
                            };

                            var text = brush switch
                            {
                                null => null,
                                SolidColorBrush solid when solid.Color.A == 0 || solid.Opacity == 0 => null,
                                SolidColorBrush solid => $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}" + (solid.Opacity < 1 ? $"@{solid.Opacity:0.00}" : string.Empty),
                                _ => brush.GetType().Name
                            };

                            if (text != null || element is Controls.Chats.ChatBackgroundControl)
                            {
                                var fe = element as FrameworkElement;
                                layers.Add($"{element.GetType().Name}{(string.IsNullOrEmpty(fe?.Name) ? string.Empty : "#" + fe.Name)}={text ?? "-"}{(element.Opacity < 1 ? $" op{element.Opacity:0.00}" : string.Empty)}");
                            }
                        }

                        return string.Join(" > ", layers);
                    }

                // Raw DWM experiments against the main window, for comparing backdrops without a
                // rebuild: accent <state> <AABBGGRR hex> <flags>, sbt <DWMSBT>, extend <margin>.
                case "accent":
                case "sbt":
                case "extend":
                    {
                        var hwnd = IntPtr.Zero;
                        foreach (var island in IslandWindow.All)
                        {
                            hwnd = island.Handle;
                            break;
                        }

                        if (parts[0] == "sbt")
                        {
                            var type = int.Parse(parts[1]);
                            return "hr=" + Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
                        }

                        if (parts[0] == "extend")
                        {
                            var margin = int.Parse(parts[1]);
                            var margins = new MARGINS { cxLeftWidth = margin, cxRightWidth = margin, cyTopHeight = margin, cyBottomHeight = margin };
                            return "hr=" + Win32.DwmExtendFrameIntoClientArea(hwnd, ref margins);
                        }

                        var args = parts.Length > 2 ? parts[2].Split(' ') : Array.Empty<string>();
                        var tint = args.Length > 0 ? Convert.ToUInt32(args[0], 16) : 0u;
                        var flags = args.Length > 1 ? int.Parse(args[1]) : 2;
                        return "ok=" + SetAccent(hwnd, int.Parse(parts[1]), tint, flags);
                    }

                case "pane":
                    {
                        var rootWindow = window.Content as Views.Host.RootWindow;
                        if (rootWindow == null)
                        {
                            Walk(root, element =>
                            {
                                rootWindow = element as Views.Host.RootWindow;
                                return rootWindow != null;
                            });
                        }

                        if (rootWindow == null)
                        {
                            return "error: no root window";
                        }

                        rootWindow.IsPaneOpen = parts[1] == "1";
                        return "ok " + rootWindow.IsPaneOpen;
                    }

                case "import-original":
                    {
                        // The colour scheme worn in the user's Store install, read out of its
                        // settings.dat: Night with a purple accent, a four-stop outgoing gradient
                        // and a dark pattern. Created as a variant of Night - the same thing the
                        // themes page makes with "+" - and worn, so it shows up as the user's own.
                        if (navigation is not TLNavigationService tl)
                        {
                            return "error: no client";
                        }

                        var settings = new Telegram.Td.Api.ThemeSettings(
                            new Telegram.Td.Api.BuiltInThemeNight(),
                            0x917BBD,
                            Services.Settings.ThemeSettingsStore.LoadBackground("LqK3YYoX2EnrCgAAJ63F4zvk4wg", 5321028841211169515, true),
                            Telegram.Td.Api.TdBackground.FromString("258DE5~4272DF~8146D7~9F3EAA"),
                            false,
                            false,
                            0x917BBD);

                        var id = AppSettings.Appearance.CreateVariant(Services.Settings.TelegramThemeType.Night, settings);

                        AppSettings.Appearance.ForceNightMode = true;
                        AppSettings.Appearance.RequestedTheme = Services.Settings.TelegramTheme.Dark;
                        AppSettings.Appearance.WearVariant(Services.Settings.TelegramThemeType.Night, id, null, true);
                        NightModeService.Current.Show(Services.Settings.TelegramTheme.Dark, window.Content.XamlRoot);
                        Telegram.ViewModels.Settings.ThemeData.SendDefaultBackground(tl.ClientService, settings.Background, true);

                        // Folders as tabs on top, and the adaptive wide layout, as in that install.
                        AppSettings.UseLeftTabsForChats = false;
                        AppSettings.IsAdaptiveWideEnabled = true;
                        EventAggregator.Current.Publish(new Services.Updates.UpdateChatFoldersLayout());

                        return "ok variant " + id;
                    }

                case "night":
                    // What the appearance page's night mode switch does, minus the toast.
                    var dark = parts[1] == "1";
                    AppSettings.Appearance.ForceNightMode = dark;
                    AppSettings.Appearance.RequestedTheme = dark ? Services.Settings.TelegramTheme.Dark : Services.Settings.TelegramTheme.Light;
                    NightModeService.Current.Update();
                    return "ok";

                case "state":
                    var appearance = AppSettings.Appearance;
                    return $"material={appearance.WindowMaterial} background={appearance.BackgroundOpacity} wallpaper={appearance.WallpaperOpacity} theme={window.ActualTheme}";

                case "names":
                    {
                        // Every named element, to find what to point the other commands at.
                        var names = new List<string>();
                        Walk(root, element =>
                        {
                            var name = AutomationProperties.GetName(element);
                            if (!string.IsNullOrEmpty(name) && element is Slider or ComboBox)
                            {
                                names.Add(element.GetType().Name + ":" + name);
                            }
                            return false;
                        });
                        return string.Join(" | ", names);
                    }

                default:
                    return "error: unknown command";
            }
        }

        private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            T found = null;
            Walk(root, element =>
            {
                if (element is T match && AutomationProperties.GetName(element) == name)
                {
                    found = match;
                    return true;
                }
                return false;
            });

            return found ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}'");
        }

        private static bool Walk(DependencyObject node, Func<FrameworkElement, bool> visit)
        {
            if (node == null)
            {
                return false;
            }

            if (node is FrameworkElement element && visit(element))
            {
                return true;
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (Walk(VisualTreeHelper.GetChild(node, i), visit))
                {
                    return true;
                }
            }

            return false;
        }

        private static unsafe bool SetAccent(IntPtr hwnd, int state, uint tint, int flags)
        {
            var accent = new ACCENT_POLICY { AccentState = state, AccentFlags = flags, GradientColor = tint };
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attrib = Win32.WCA_ACCENT_POLICY, Data = (IntPtr)(&accent), SizeOfData = sizeof(ACCENT_POLICY) };
            return Win32.SetWindowCompositionAttribute(hwnd, ref data);
        }

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
