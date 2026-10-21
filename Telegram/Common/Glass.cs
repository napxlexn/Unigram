//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System.Collections.Generic;
using Telegram.Services;
using Telegram.Services.Settings;
using Telegram.Views.Popups;
using Windows.UI;

namespace Telegram.Common
{
    /// <summary>
    /// What a kind of glass is made of, for whichever element wears it.
    /// </summary>
    public sealed class GlassSpec
    {
        /// <summary>
        /// The tint over the blur. Null where the element's own colour is the tint - a bubble
        /// keeps the theme's bubble colour, made translucent.
        /// </summary>
        public Color? Tint { get; init; }

        /// <summary>
        /// The blur of what is behind, before the strength setting; 0 is none.
        /// </summary>
        public float Blur { get; init; }

        public float Saturation { get; init; } = 1.7f;

        /// <summary>
        /// Light from inside the glass, strongest in its middle: a lamp behind frosted glass.
        /// </summary>
        public Color? InnerLight { get; init; }

        public bool Scanlines { get; init; }

        /// <summary>
        /// Whether the content glows, and in what colour; transparent for its own colour.
        /// </summary>
        public Color? Phosphor { get; init; }

        /// <summary>
        /// A rim of its own, in place of the rim style - the neon tube, the ice edge.
        /// </summary>
        public Color? RimColor { get; init; }

        public bool RimGlows { get; init; }

        /// <summary>
        /// A soft wash of colours over the glass, top left to bottom right.
        /// </summary>
        public Color[] Wash { get; init; }

        public bool IsOriginal { get; init; }
    }

    /// <summary>
    /// The one list of glass, see <see cref="GlassKind"/>.
    /// </summary>
    public static class Glass
    {
        public static List<SettingsOptionItem<GlassKind>> Options => new()
        {
            new(GlassKind.Original, "Оригінал"),
            new(GlassKind.Clear, "Прозоре скло"),
            new(GlassKind.Frosted, "Матове скло"),
            new(GlassKind.Liquid, "Рідке скло"),
            new(GlassKind.Dark, "Темне скло"),
            new(GlassKind.Light, "Світле скло"),
            new(GlassKind.Smoke, "Димчасте скло"),
            new(GlassKind.Ice, "Крижане скло"),
            new(GlassKind.Pearl, "Перламутр"),
            new(GlassKind.Aurora, "Аврора"),
            new(GlassKind.Glow, "Лампове сяйво"),
            new(GlassKind.Candle, "Свічка"),
            new(GlassKind.Neon, "Неон"),
            new(GlassKind.Terminal, "Ретро-термінал (зелений)"),
            new(GlassKind.Amber, "Ретро-термінал (бурштин)"),
            new(GlassKind.Solid, "Суцільний"),
        };

        public static List<SettingsOptionItem<RimStyle>> RimOptions => new()
        {
            new(RimStyle.None, "Без обідка"),
            new(RimStyle.Thin, "Тонкий"),
            new(RimStyle.Soft, "М'який"),
            new(RimStyle.TopLight, "Відблиск зверху"),
            new(RimStyle.Double, "Подвійний"),
            new(RimStyle.Glowing, "Сяйний"),
            new(RimStyle.Bold, "Виразний"),
            new(RimStyle.Iridescent, "Райдужний"),
        };

        private static readonly Color Green = Color.FromArgb(0xFF, 0x4C, 0xFF, 0x7A);
        private static readonly Color AmberColor = Color.FromArgb(0xFF, 0xFF, 0xB0, 0x3A);
        private static readonly Color Warm = Color.FromArgb(0xFF, 0xFF, 0xE0, 0xA8);

        /// <summary>
        /// What a kind is made of on a dark or a light theme. The tints are the strength of the
        /// glass; the effects are scaled by the intensity settings where they are drawn.
        /// </summary>
        public static GlassSpec Resolve(GlassKind kind, bool dark)
        {
            Color Pick(uint onDark, uint onLight)
            {
                var value = dark ? onDark : onLight;
                return Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            }

            return kind switch
            {
                GlassKind.Clear => new GlassSpec { Tint = Pick(0x30202020, 0x40FFFFFF), Blur = 14 },
                GlassKind.Frosted => new GlassSpec { Tint = Pick(0x8C161616, 0xA6FFFFFF), Blur = 30 },
                GlassKind.Liquid => new GlassSpec { Tint = Pick(0x40202024, 0x50FFFFFF), Blur = 22, Saturation = 2.0f, RimColor = Pick(0xA0FFFFFF, 0xF0FFFFFF) },
                GlassKind.Dark => new GlassSpec { Tint = Pick(0xC00C0C0C, 0x99202020), Blur = 30 },
                GlassKind.Light => new GlassSpec { Tint = Pick(0x4CFFFFFF, 0xC8FFFFFF), Blur = 30 },
                GlassKind.Smoke => new GlassSpec { Tint = Pick(0x9030302E, 0x80707070), Blur = 34, Saturation = 0.6f },
                GlassKind.Ice => new GlassSpec { Tint = Pick(0x60507090, 0x80DDEEFF), Blur = 26, Saturation = 1.2f, RimColor = Pick(0xB0C8E8FF, 0xFFFFFFFF) },
                GlassKind.Pearl => new GlassSpec { Tint = Pick(0x50303038, 0x70FFFFFF), Blur = 26, Wash = new[] { Pick(0x30FFB8E8, 0x40FFC8F0), Pick(0x30B8E0FF, 0x40C8ECFF), Pick(0x30E8FFC8, 0x40F0FFD8) } },
                GlassKind.Aurora => new GlassSpec { Tint = Pick(0x70141418, 0x90FFFFFF), Blur = 30, Wash = new[] { Pick(0x4032D0A0, 0x3032D0A0), Pick(0x403C78FF, 0x303C78FF), Pick(0x40A050FF, 0x30A050FF) } },
                GlassKind.Glow => new GlassSpec { Tint = Pick(0x8C161616, 0xA6FFFFFF), Blur = 30, InnerLight = Warm, Phosphor = Colors.Transparent },
                GlassKind.Candle => new GlassSpec { Tint = Pick(0xA0140C06, 0x90FFF4E4), Blur = 30, InnerLight = Color.FromArgb(0xFF, 0xFF, 0xA0, 0x40), Phosphor = Color.FromArgb(0xFF, 0xFF, 0xC0, 0x70) },
                GlassKind.Neon => new GlassSpec { Tint = Pick(0xB00A0A10, 0x90FFFFFF), Blur = 30, Phosphor = Colors.Transparent, RimColor = dark ? Theme.AccentDark.Default : Theme.AccentLight.Default, RimGlows = true },
                GlassKind.Terminal => new GlassSpec { Tint = Pick(0xC0060C08, 0xB0102014), Blur = 24, InnerLight = Green, Scanlines = true, Phosphor = Green },
                GlassKind.Amber => new GlassSpec { Tint = Pick(0xC00E0904, 0xB0201408), Blur = 24, InnerLight = AmberColor, Scanlines = true, Phosphor = AmberColor },
                GlassKind.Solid => new GlassSpec { Tint = Pick(0xFF1C1C1C, 0xFFF6F6F6), Blur = 0 },
                _ => new GlassSpec { IsOriginal = true },
            };
        }

        /// <summary>
        /// Kinds whose tint is the glass's own; the rest take the element's colour where it has
        /// one - a bubble stays its theme colour under frosted or liquid glass.
        /// </summary>
        public static bool HasOwnTint(GlassKind kind)
        {
            return kind is GlassKind.Dark or GlassKind.Light or GlassKind.Smoke or GlassKind.Ice
                or GlassKind.Terminal or GlassKind.Amber or GlassKind.Neon or GlassKind.Candle;
        }

        /// <summary>
        /// The blur after the strength setting: 60% is the designed amount, 0 none, 100 about
        /// twice as much.
        /// </summary>
        public static float ScaleBlur(float blur)
        {
            return blur * AppSettings.Appearance.BlurIntensity / 60f;
        }
    }
}
