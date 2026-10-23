//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Numerics;
using Telegram.Common;
using Telegram.Navigation;
using Telegram.Services;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;

namespace Telegram.Controls.Media
{
    /// <summary>
    /// Glass that fades out by growing weaker, not only more transparent: layers of the backdrop
    /// blurred less and less, each reaching a step further out under an eased mask of its own,
    /// the strongest on top where the glass is solid, and the tint fading over all of them.
    ///
    /// A single blur fading by its alpha - <see cref="FadingGlassBrush"/> - lays a ghost of
    /// itself over the sharp content all through the fade, which reads as coarse. Drawn as
    /// visuals, one mask brush each, because an effect graph that takes gradients as its masks
    /// is one the compositor drops, and every visual near it with it.
    /// </summary>
    public static class ProgressiveGlass
    {
        /// <param name="Steps">How many blurs, each weaker, the fade is made of.</param>
        /// <param name="Curve">The power the strength grows with from the weakest to the strongest.</param>
        /// <param name="Overlap">Over how many steps each layer eases out.</param>
        /// <param name="Ease">The fade's form: smoothstep, linear, smootherstep, quick or slow fall.</param>
        /// <param name="Tint">How much of the tint's own alpha is drawn.</param>
        /// <param name="TintSpread">How much of the fade the tint takes to go.</param>
        /// <param name="Outline">Marks where the glass is solid to and where it ends.</param>
        public sealed record Shape(int Steps = 6, float Curve = 2, float Overlap = 2, int Ease = 0, float Saturation = 1.2f, bool Quality = true, float Tint = 1, float TintSpread = 1, bool Outline = false);

        /// <param name="solidUntil">Where the fade starts, as a fraction of the height from the
        /// solid side.</param>
        /// <param name="tintUntil">Where the tint starts to fade, likewise.</param>
        /// <param name="fadesUp">Solid at the bottom, fading upwards.</param>
        public static void Attach(FrameworkElement host, Color tint, float blur, float solidUntil, float tintUntil, bool fadesUp, Shape shape)
        {
            Detach(host);

            var compositor = BootStrapper.Current.Compositor;
            var root = compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;

            var steps = Math.Max(1, shape.Steps);

            solidUntil = Math.Clamp(solidUntil, 0, 1);
            tintUntil = Math.Clamp(tintUntil, 0, 1);

            var fade = 1 - solidUntil;

            if (PowerSavingPolicy.AreMaterialsEnabled && blur > 0)
            {
                // From the weakest, reaching furthest, to the strongest, drawn last and on top;
                // each holds a step further out than the next stronger one, then eases out over
                // the overlap, so that neighbours overlap and no line shows between them.
                // The strength grows with a power of the step: the outer layers barely blur,
                // so the content goes soft gradually rather than at the edge of the band.
                for (int k = 1; k <= steps; k++)
                {
                    var holds = solidUntil + fade * (steps - k) / steps;
                    var strength = MathF.Pow((float)k / steps, Math.Max(0.01f, shape.Curve));

                    var masked = compositor.CreateMaskBrush();
                    masked.Source = SolidGaussianBrush.CreateBrush(compositor, Colors.Transparent, blur * strength, shape.Saturation, quality: shape.Quality);
                    masked.Mask = CreateRamp(compositor, holds, holds + fade * shape.Overlap / steps, fadesUp, shape.Ease);

                    root.Children.InsertAtTop(CreateLayer(compositor, masked));
                }
            }

            if (shape.Tint > 0 && tint.A > 0)
            {
                tint.A = (byte)Math.Clamp(tint.A * shape.Tint, 0, 255);

                var tinted = compositor.CreateMaskBrush();
                tinted.Source = compositor.CreateColorBrush(tint);
                tinted.Mask = CreateRamp(compositor, tintUntil, tintUntil + (1 - tintUntil) * shape.TintSpread, fadesUp, shape.Ease);

                root.Children.InsertAtTop(CreateLayer(compositor, tinted));
            }

            if (shape.Outline)
            {
                // Yellow where the fade starts, cyan where the tint does, red at the far edge.
                root.Children.InsertAtTop(CreateLine(compositor, Colors.Yellow, solidUntil, fadesUp));
                root.Children.InsertAtTop(CreateLine(compositor, Colors.Cyan, tintUntil, fadesUp));
                root.Children.InsertAtTop(CreateLine(compositor, Colors.Red, 1, fadesUp));
            }

            ElementCompositionPreview.SetElementChildVisual(host, root);
            host.Tag = new Attachment();
        }

        public static void Detach(FrameworkElement host)
        {
            if (host?.Tag is Attachment)
            {
                host.Tag = null;

                // Released rather than disposed: the compositor may still be drawing it.
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
        }

        private sealed record Attachment;

        // Sized with the host by the relative adjustments, so no handler has to follow it.
        private static SpriteVisual CreateLayer(Compositor compositor, CompositionBrush brush)
        {
            var layer = compositor.CreateSpriteVisual();
            layer.Brush = brush;
            layer.RelativeSizeAdjustment = Vector2.One;
            return layer;
        }

        private static SpriteVisual CreateLine(Compositor compositor, Color color, float at, bool fadesUp)
        {
            var line = compositor.CreateSpriteVisual();
            line.Brush = compositor.CreateColorBrush(color);
            line.RelativeSizeAdjustment = new Vector2(1, 0);
            line.Size = new Vector2(0, 2);
            line.RelativeOffsetAdjustment = new Vector3(0, fadesUp ? 1 - at : at, 0);
            line.Offset = new Vector3(0, fadesUp ? (at >= 1 ? 0 : -1) : (at >= 1 ? -2 : -1), 0);
            return line;
        }

        private static float Eased(float t, int ease)
        {
            return ease switch
            {
                1 => 1 - t,
                2 => 1 - t * t * t * (t * (t * 6 - 15) + 10),
                3 => (1 - t) * (1 - t),
                4 => 1 - t * t,
                _ => 1 - t * t * (3 - 2 * t)
            };
        }

        /// <summary>
        /// Opaque from the solid side to <paramref name="from"/>, eased to nothing by
        /// <paramref name="to"/> in eight stops, so it has no visible start or end.
        /// </summary>
        private static CompositionLinearGradientBrush CreateRamp(Compositor compositor, float from, float to, bool fadesUp, int ease)
        {
            from = Math.Clamp(from, 0, 0.999f);
            to = Math.Clamp(to, from + 0.001f, 1);

            var ramp = compositor.CreateLinearGradientBrush();
            ramp.StartPoint = new Vector2(0, fadesUp ? 1 : 0);
            ramp.EndPoint = new Vector2(0, fadesUp ? 0 : 1);
            ramp.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(255, 255, 255, 255)));

            for (int i = 0; i <= 8; i++)
            {
                var t = i / 8f;
                ramp.ColorStops.Add(compositor.CreateColorGradientStop(from + (to - from) * t, Color.FromArgb((byte)(255 * Math.Clamp(Eased(t, ease), 0, 1)), 255, 255, 255)));
            }

            if (to < 1)
            {
                ramp.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255)));
            }

            return ramp;
        }
    }
}
