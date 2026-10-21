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
    /// under a frosted bubble, the rim of a liquid one, the glow of a lamp one - is handed to
    /// the bubble by <see cref="GetFrostedBrush"/>, <see cref="GetLiquidRimBrush"/> and
    /// <see cref="ContentGlow"/> and picked up as bubbles are realized, which is when the chat
    /// is opened or scrolled.
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
        public static Color Adjust(string key, Color color, TelegramTheme parent, bool outgoing)
        {
            var appearance = AppSettings.Appearance;
            var dark = parent == TelegramTheme.Dark;

            // Own colours first, replacing the theme's; the glass styles below then apply to
            // them exactly as they would to the theme's own.
            if (appearance.UseCustomMessageColors)
            {
                var text = outgoing ? appearance.CustomOutgoingTextColor : appearance.CustomIncomingTextColor;

                color = key switch
                {
                    "MessageBackgroundBrush" => outgoing ? appearance.CustomOutgoingColor : appearance.CustomIncomingColor,
                    "MessageForegroundBrush" => text,
                    "MessageSubtleLabelBrush" or "MessageSubtleGlyphBrush" or "MessageSubtleForegroundBrush" => WithAlpha(text, 0x99),
                    "MessageReactionBackgroundBrush" => appearance.CustomReactionColor,
                    "MessageReactionForegroundBrush" => text,
                    "MessageReactionChosenBackgroundBrush" => appearance.CustomOutgoingColor,
                    "MessageReactionChosenForegroundBrush" => appearance.CustomOutgoingTextColor,
                    _ => color
                };
            }

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
                    // No hard rim on any style: the glowing one lights the capsule from inside
                    // instead (MessageReactionGlowBrush, see ApplyReactionGlow).
                    color = Color.FromArgb(0, 0, 0, 0);
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

        private static Color WithAlpha(Color color, byte alpha)
        {
            color.A = alpha;
            return color;
        }

        #region Bubbles

        private static readonly Dictionary<Color, Brush> _frosted = new();
        private static readonly Dictionary<bool, Brush> _glow = new();

        public static bool IsFrosted => AppSettings.Appearance.BubbleStyle is BubbleStyle.Frosted or BubbleStyle.Liquid;

        public static bool IsLiquid => AppSettings.Appearance.BubbleStyle == BubbleStyle.Liquid;

        /// <summary>
        /// The lamp styles, whose content glows - see <see cref="ContentGlow"/>.
        /// </summary>
        public static bool IsGlowing => AppSettings.Appearance.BubbleStyle is BubbleStyle.Glow or BubbleStyle.Terminal;

        public static bool IsTerminal => AppSettings.Appearance.BubbleStyle == BubbleStyle.Terminal;

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
        /// Liquid glass: light caught by the top edge of a thick pane, gone by the middle, and
        /// a hint of it again at the bottom where the light leaves.
        /// </summary>
        public static Brush GetLiquidRimBrush(bool dark)
        {
            if (!_glow.TryGetValue(dark, out var brush))
            {
                var top = dark ? Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF);
                var bottom = dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF);

                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                gradient.GradientStops.Add(new GradientStop { Color = top, Offset = 0 });
                gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0xFF, 0xFF, 0xFF), Offset = 0.45 });
                gradient.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0xFF, 0xFF, 0xFF), Offset = 0.7 });
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

        /// <summary>
        /// The lamp glow of the reaction capsules: one brush shared by all of them, its stops
        /// set in place - warm light in the middle, fading out well before the edge.
        /// </summary>
        private static void ApplyReactionGlow()
        {
            if (Application.Current.Resources.TryGetValue("MessageReactionGlowBrush", out var resource)
                && resource is Microsoft.UI.Xaml.Media.RadialGradientBrush brush
                && brush.GradientStops.Count == 2)
            {
                brush.GradientStops[0].Color = AppSettings.Appearance.ReactionStyle == ReactionStyle.Glow
                    ? Color.FromArgb(0x62, 0xFF, 0xE4, 0xB0)
                    : Color.FromArgb(0, 0xFF, 0xFF, 0xFF);
            }
        }

        public static void ApplyServiceStyle()
        {
            ApplyReactionGlow();

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
