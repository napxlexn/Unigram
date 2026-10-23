//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Telegram.Common;
using Telegram.Navigation;
using Telegram.Services;
using Telegram.Services.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace Telegram.Controls
{
    /// <summary>
    /// The glass tuner: every value of <see cref="GlassTuning"/> as a slider, in a window of its
    /// own beside the chat, each applied the moment it moves.
    /// </summary>
    public sealed partial class GlassTuner : UserControl
    {
        private static WindowContext _window;

        // What shows each value, to follow it when it changes from elsewhere.
        private readonly Dictionary<string, (Action<double> Show, TextBlock Value)> _rows = new();

        private GlassTuner()
        {
            var panel = new StackPanel
            {
                Padding = new Thickness(16, 8, 16, 24),
                Spacing = 4
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Налагодження скла",
                Style = Application.Current.Resources["TitleTextBlockStyle"] as Style
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Кожна зміна одразу з'являється в чаті. Значення зберігаються.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };

            var copy = new Button { Content = "Копіювати значення" };
            copy.Click += Copy_Click;
            AutomationProperties.SetName(copy, "Копіювати значення");
            actions.Children.Add(copy);

            var reset = new Button { Content = "Скинути все" };
            reset.Click += ResetAll_Click;
            AutomationProperties.SetName(reset, "Скинути все");
            actions.Children.Add(reset);

            panel.Children.Add(actions);

            string group = null;

            foreach (var parameter in GlassTuning.All)
            {
                if (parameter.Group != group)
                {
                    group = parameter.Group;
                    panel.Children.Add(new TextBlock
                    {
                        Text = group,
                        Style = Application.Current.Resources["BaseTextBlockStyle"] as Style,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 16, 0, 4)
                    });
                }

                panel.Children.Add(CreateRow(parameter));
            }

            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled,
                Margin = new Thickness(0, 32, 0, 0)
            };

            Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;

            GlassTuning.Changed += OnChanged;
            Unloaded += (s, e) => GlassTuning.Changed -= OnChanged;
        }

        private FrameworkElement CreateRow(GlassTuning.Parameter parameter)
        {
            var value = GlassTuning.Get(parameter.Key);

            var label = new TextBlock
            {
                Text = parameter.Label,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            var shown = new TextBlock
            {
                Text = parameter.Format(value),
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };

            var reset = new Button
            {
                Content = "↺",
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(reset, "Типове значення: " + parameter.Format(parameter.Default));
            AutomationProperties.SetName(reset, "Скинути " + parameter.Key);
            reset.Click += (s, e) => GlassTuning.Reset(parameter.Key);

            FrameworkElement editor;

            switch (parameter.Kind)
            {
                case GlassTuning.ParameterKind.Toggle:
                    {
                        var toggle = new ToggleSwitch { IsOn = value >= 0.5, OnContent = string.Empty, OffContent = string.Empty };
                        toggle.Toggled += (s, e) => GlassTuning.Set(parameter.Key, toggle.IsOn ? 1 : 0);
                        _rows[parameter.Key] = (x => toggle.IsOn = x >= 0.5, shown);
                        editor = toggle;
                        break;
                    }

                case GlassTuning.ParameterKind.Choice:
                    {
                        var combo = new ComboBox { ItemsSource = parameter.Choices, SelectedIndex = (int)value, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 4) };
                        combo.SelectionChanged += (s, e) =>
                        {
                            if (combo.SelectedIndex >= 0)
                            {
                                GlassTuning.Set(parameter.Key, combo.SelectedIndex);
                            }
                        };
                        _rows[parameter.Key] = (x => combo.SelectedIndex = (int)x, shown);
                        editor = combo;
                        break;
                    }

                default:
                    {
                        var slider = new Slider
                        {
                            Minimum = parameter.Minimum,
                            Maximum = parameter.Maximum,
                            StepFrequency = parameter.Step,
                            SmallChange = parameter.Step,
                            LargeChange = parameter.Step * 10,
                            Value = value,
                            IsThumbToolTipEnabled = false
                        };
                        slider.ValueChanged += (s, e) => GlassTuning.Set(parameter.Key, e.NewValue);
                        _rows[parameter.Key] = (x => slider.Value = x, shown);
                        editor = slider;
                        break;
                    }
            }

            AutomationProperties.SetName(editor, parameter.Key);

            // A switch or a list shows its value itself.
            if (parameter.Kind != GlassTuning.ParameterKind.Slider)
            {
                shown.Visibility = Visibility.Collapsed;
            }

            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetColumn(shown, 1);
            Grid.SetColumn(reset, 2);
            Grid.SetRow(editor, 1);
            Grid.SetColumnSpan(editor, 3);

            row.Children.Add(label);
            row.Children.Add(shown);
            row.Children.Add(editor);

            // A style or a kind of glass has no default of its own here: it is the appearance
            // page's choice, which a reset would undo.
            if (parameter.Kind != GlassTuning.ParameterKind.Choice || !parameter.IsBound)
            {
                row.Children.Add(reset);
            }

            return row;
        }

        // From a slider of this window, a reset, or another tuner: the row shows what is stored.
        private void OnChanged(object sender, string key)
        {
            this.BeginOnUIThread(() =>
            {
                if (_rows.TryGetValue(key, out var row) && GlassTuning.Find(key) is GlassTuning.Parameter parameter)
                {
                    var value = GlassTuning.Get(key);
                    row.Value.Text = parameter.Format(value);
                    row.Show(value);
                }
            });
        }

        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var parameter in GlassTuning.All)
            {
                if (parameter.Kind != GlassTuning.ParameterKind.Choice || !parameter.IsBound)
                {
                    GlassTuning.Reset(parameter.Key);
                }
            }
        }

        // Every value as text, to send on to whoever bakes them in as the defaults.
        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var text = new StringBuilder();

            foreach (var parameter in GlassTuning.All)
            {
                var value = GlassTuning.Get(parameter.Key);
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} = {1}{2}", parameter.Key, value, value == parameter.Default ? string.Empty : "  (типово " + parameter.Default.ToString(CultureInfo.InvariantCulture) + ")"));
            }

            var package = new DataPackage();
            package.SetText(text.ToString());
            Clipboard.SetContent(package);

            if (sender is Button button)
            {
                button.Content = "Скопійовано";
            }
        }

        /// <summary>
        /// Opens the tuner, or brings forward the one already open.
        /// </summary>
        public static async void Open()
        {
            if (_window != null)
            {
                var open = _window;
                open.Dispatcher.Dispatch(open.Activate);
                return;
            }

            var window = await new ViewService().OpenAsync(new ViewServiceOptions
            {
                Title = "Налагодження скла",
                Width = 420,
                Height = 760,
                PersistedId = "GlassTuner",
                Content = context => new GlassTuner()
            });

            if (window != null)
            {
                _window = window;
                window.Closed += (s, e) => _window = null;
            }
        }
    }
}
