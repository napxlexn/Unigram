//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using Telegram.Controls.Media;
using Telegram.Services;
using Telegram.Services.Settings;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Telegram.Common
{
    /// <summary>
    /// The glass styles of the chat: bubbles, reaction capsules, service pills and text.
    ///
    /// Most of it rides on the per-direction message brushes, which <see cref="MessageBrushes"/>
    /// recolours in place on every theme pass: <see cref="Adjust"/> is applied there, so a
    /// change shows on every visible bubble at once. What a colour cannot express - the blur
    /// under a frosted bubble, the rim of a glowing one - is handed to the bubble by
    /// <see cref="GetFrostedBrush"/> and <see cref="GetGlowBrush"/> and picked up as bubbles are
    /// realized, which is when the chat is opened or scrolled.
    /// </summary>
    public static class MessageStyle
    {
        /// <summary>
        /// Bumped on every change, so a recycled bubble can tell its fill is stale.
        /// </summary>
        public static int Version { get; private set; }

        public static void Invalidate()
        {
            Version++;
            _frosted.Clear();
            _glow.Clear();
            ApplyServiceStyle();
        }

        /// <summary>
        /// The colour a message brush is drawn in under the chosen styles.
        /// </summary>
        public static Color Adjust(string key, Color color, TelegramTheme parent)
        {
            var appearance = AppSettings.Appearance;
            var dark = parent == TelegramTheme.Dark;

            switch (key)
            {
                case "MessageBackgroundBrush" when appearance.BubbleStyle != BubbleStyle.Original:
                    color.A = (byte)(color.A * appearance.BubbleOpacity / 100);
                    break;

                case "MessageReactionBackgroundBrush" when appearance.ReactionStyle != ReactionStyle.Original:
                    color.A = (byte)(color.A * 0.55);
                    break;

                case "MessageReactionChosenBackgroundBrush" when appearance.ReactionStyle != ReactionStyle.Original:
                    color.A = (byte)(color.A * 0.8);
                    break;

                case "MessageReactionBorderBrush":
                    // A light rim on dark, a faint dark one on light; nothing unless glowing.
                    color = appearance.ReactionStyle == ReactionStyle.Glow
                        ? dark ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00)
                        : Color.FromArgb(0, 0, 0, 0);
                    break;

                case "MessageForegroundBrush" when appearance.MessageTextStyle == MessageTextStyle.Brighter:
                    color = dark ? Colors.White : Colors.Black;
                    break;

                case "MessageForegroundBrush" when appearance.MessageTextStyle == MessageTextStyle.Softer:
                    color.A = 0xC8;
                    break;
            }

            return color;
        }

        #region Bubbles

        private static readonly Dictionary<Color, Brush> _frosted = new();
        private static readonly Dictionary<bool, Brush> _glow = new();

        public static bool IsFrosted => AppSettings.Appearance.BubbleStyle == BubbleStyle.Frosted;

        public static bool IsGlowing => AppSettings.Appearance.BubbleStyle == BubbleStyle.Glow;

        /// <summary>
        /// A blur of what is behind the bubble under its own translucent colour. One brush per
        /// colour, shared by every bubble of that colour: the blur still runs per bubble, but
        /// the effect graph is built once.
        /// </summary>
        public static Brush GetFrostedBrush(Color tint)
        {
            if (!_frosted.TryGetValue(tint, out var brush))
            {
                var fallback = tint;
                fallback.A = 0xFF;

                _frosted[tint] = brush = new SolidGaussianBrush { TintColor = tint, FallbackColor = fallback };
            }

            return brush;
        }

        /// <summary>
        /// A soft rim: bright along the top edge, almost gone at the bottom, as light catching
        /// the edge of a pane of glass.
        /// </summary>
        public static Brush GetGlowBrush(bool dark)
        {
            if (!_glow.TryGetValue(dark, out var brush))
            {
                var top = dark ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF);
                var bottom = dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00);

                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                gradient.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
                gradient.GradientStops.Add(new GradientStop { Color = bottom, Offset = 1 });

                _glow[dark] = brush = gradient;
            }

            return brush;
        }

        #endregion

        #region Service pills

        // The pills' brush is the app's, declared once per theme in Accent.xaml, and every pill
        // in every chat paints with that one instance - so retinting it restyles all of them.
        private static readonly Dictionary<SolidGaussianBrush, Color> _serviceOriginal = new();

        public static void ApplyServiceStyle()
        {
            foreach (var (theme, brush) in FindServiceBrushes())
            {
                if (!_serviceOriginal.TryGetValue(brush, out var original))
                {
                    _serviceOriginal[brush] = original = brush.TintColor;
                }

                var light = theme == "Light";
                brush.TintColor = AppSettings.Appearance.ServiceStyle switch
                {
                    ServiceStyle.DarkGlass => Color.FromArgb(0x99, 0x10, 0x10, 0x10),
                    ServiceStyle.LightGlass => light ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF),
                    ServiceStyle.ClearGlass => Color.FromArgb(0x24, 0x80, 0x80, 0x80),
                    _ => original
                };
            }
        }

        private static IEnumerable<(string Theme, SolidGaussianBrush Brush)> FindServiceBrushes()
        {
            var found = new List<(string, SolidGaussianBrush)>();
            Collect(Application.Current.Resources, found);
            return found;
        }

        private static void Collect(ResourceDictionary dictionary, List<(string, SolidGaussianBrush)> found)
        {
            foreach (var theme in new[] { "Dark", "Light", "Default" })
            {
                if (dictionary.ThemeDictionaries.TryGetValue(theme, out var value)
                    && value is ResourceDictionary themed
                    && themed.TryGetValue("MessageServiceBackgroundBrush", out var resource)
                    && resource is SolidGaussianBrush brush)
                {
                    found.Add((theme, brush));
                }
            }

            foreach (var merged in dictionary.MergedDictionaries)
            {
                Collect(merged, found);
            }
        }

        #endregion
    }
}
