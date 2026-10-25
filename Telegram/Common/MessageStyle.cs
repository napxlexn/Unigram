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
    /// change shows on every visible bubble at once. What a colour cannot express - the blur,
    /// the rim, the light, the glow of the text - is drawn by <see cref="SmoothGlass"/> and
    /// <see cref="ContentGlow"/> on each bubble, picked up as bubbles are realized, which is
    /// when the chat is opened or scrolled.
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

            // A screen of one phosphor - the terminals, the candle - draws the count in it too,
            // and a chosen reaction lit in it rather than in the theme's colour.
            var phosphor = appearance.ReactionGlass != GlassKind.Original
                && Glass.Resolve(appearance.ReactionGlass, dark).Phosphor is Color mono
                && mono.A > 0
                    ? mono
                    : (Color?)null;

            switch (key)
            {
                case "MessageReactionForegroundBrush" or "MessageReactionChosenForegroundBrush" when phosphor is Color lit:
                    color = lit;
                    break;

                case "MessageReactionChosenBackgroundBrush" when phosphor is Color lit:
                    color = WithAlpha(lit, 0x48);
                    break;

                // Neon is lit by its tube, the rim: a chosen capsule only takes on its colour.
                case "MessageReactionChosenBackgroundBrush" when appearance.ReactionGlass == GlassKind.Neon:
                    color = WithAlpha(color, 0x50);
                    break;

                case "MessageBackgroundBrush" when appearance.BubbleGlass != GlassKind.Original:
                    color.A = (byte)(color.A * appearance.BubbleOpacity / 100);
                    break;

                case "MessageReactionBackgroundBrush" when appearance.ReactionGlass != GlassKind.Original:
                    // Glass of its own colour where the kind has one; the theme's, thinned, where not.
                    color = Glass.HasOwnTint(appearance.ReactionGlass) && Glass.Resolve(appearance.ReactionGlass, dark).Tint is Color own
                        ? own
                        : WithAlpha(color, (byte)(color.A * 0.55));
                    break;

                case "MessageReactionChosenBackgroundBrush" when appearance.ReactionGlass != GlassKind.Original:
                    color.A = (byte)(color.A * 0.8);
                    break;

                case "MessageForegroundBrush" when appearance.MessageTextStyle == MessageTextStyle.Brighter:
                    color = dark ? Colors.White : Colors.Black;
                    break;

                case "MessageForegroundBrush" when appearance.MessageTextStyle == MessageTextStyle.Softer:
                    color.A = 0xC8;
                    break;
            }

            return Theme.Saturate(color);
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            color.A = alpha;
            return color;
        }

        #region Bubbles

        /// <summary>
        /// The bubbles' glass. Anything but the original is drawn by SmoothGlass behind the
        /// content - see MessageBubble.UpdateContentPanelState.
        /// </summary>
        public static GlassKind BubbleKind => AppSettings.Appearance.BubbleGlass;

        /// <summary>
        /// The colour the content of a bubble glows in under the bubbles' glass, or null for none:
        /// transparent for the content's own colour.
        /// </summary>
        public static Color? GetBubblePhosphor(bool dark)
        {
            return BubbleKind == GlassKind.Original ? null : Glass.Resolve(BubbleKind, dark).Phosphor;
        }

        #endregion

        #region Service pills

        // The pills' brush is the app's, declared once per theme in Accent.xaml, and every pill
        // in every chat paints with that one instance - so retinting it restyles all of them.
        private static readonly Dictionary<SolidGaussianBrush, Color> _serviceOriginal = new();

        /// <summary>
        /// The light inside the reaction capsules, for kinds that have one: one brush shared by
        /// all of them, its stops set in place - light in the middle, fading out well before the
        /// edge, as strong as the glow setting says.
        /// </summary>
        private static void ApplyReactionGlow()
        {
            if (Application.Current.Resources.TryGetValue("MessageReactionGlowBrush", out var resource)
                && resource is Microsoft.UI.Xaml.Media.RadialGradientBrush brush
                && brush.GradientStops.Count == 2)
            {
                var appearance = AppSettings.Appearance;
                var light = appearance.ReactionGlass != GlassKind.Original
                    ? Glass.Resolve(appearance.ReactionGlass, true).InnerLight
                    : null;

                brush.GradientStops[0].Color = light is Color color
                    ? WithAlpha(color, (byte)(0xA0 * appearance.ReactionGlowIntensity / 100))
                    : Color.FromArgb(0, 0xFF, 0xFF, 0xFF);
            }
        }

        public static void ApplyServiceStyle()
        {
            ApplyReactionGlow();

            var kind = AppSettings.Appearance.ServiceGlass;

            foreach (var (theme, brush) in FindServiceBrushes())
            {
                if (!_serviceOriginal.TryGetValue(brush, out var original))
                {
                    _serviceOriginal[brush] = original = brush.TintColor;
                }

                brush.TintColor = kind == GlassKind.Original
                    ? original
                    : Glass.Resolve(kind, theme != "Light").Tint ?? original;
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
