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
        /// <param name="solidUntil">Where the fade starts, as a fraction of the height from the
        /// solid side.</param>
        /// <param name="fadesUp">Solid at the bottom, fading upwards.</param>
        public static void Attach(FrameworkElement host, Color tint, float blur, float solidUntil, bool fadesUp, int steps = 6)
        {
            Detach(host);

            var compositor = BootStrapper.Current.Compositor;
            var root = compositor.CreateContainerVisual();
            var layers = new SpriteVisual[steps + 1];

            var fade = 1 - solidUntil;

            if (PowerSavingPolicy.AreMaterialsEnabled && blur > 0)
            {
                // From the weakest, reaching furthest, to the strongest, drawn last and on top;
                // each holds a step further out than the next stronger one, then eases out over
                // two steps, so that neighbours overlap and no line shows between them.
                // The strength grows with the square of the step: the outer layers barely blur,
                // so the content goes soft gradually rather than at the edge of the band.
                for (int k = 1; k <= steps; k++)
                {
                    var holds = solidUntil + fade * (steps - k) / steps;
                    var strength = (float)k / steps;

                    var masked = compositor.CreateMaskBrush();
                    masked.Source = SolidGaussianBrush.CreateBrush(compositor, Colors.Transparent, blur * strength * strength, 1.2f, quality: true);
                    masked.Mask = CreateRamp(compositor, holds, holds + fade * 2f / steps, fadesUp);

                    layers[k - 1] = compositor.CreateSpriteVisual();
                    layers[k - 1].Brush = masked;
                    root.Children.InsertAtTop(layers[k - 1]);
                }
            }

            var tinted = compositor.CreateMaskBrush();
            tinted.Source = compositor.CreateColorBrush(tint);
            tinted.Mask = CreateRamp(compositor, solidUntil, 1, fadesUp);

            layers[steps] = compositor.CreateSpriteVisual();
            layers[steps].Brush = tinted;
            root.Children.InsertAtTop(layers[steps]);

            void Resize(object sender, SizeChangedEventArgs e)
            {
                foreach (var layer in layers)
                {
                    layer?.Size = e.NewSize.ToVector2();
                }
            }

            foreach (var layer in layers)
            {
                layer?.Size = host.ActualSize;
            }

            host.SizeChanged += Resize;
            host.Tag = new Attachment(Resize);

            ElementCompositionPreview.SetElementChildVisual(host, root);
        }

        public static void Detach(FrameworkElement host)
        {
            if (host?.Tag is Attachment attachment)
            {
                host.SizeChanged -= attachment.Resize;
                host.Tag = null;

                // Released rather than disposed: the compositor may still be drawing it.
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
        }

        private sealed record Attachment(SizeChangedEventHandler Resize);

        /// <summary>
        /// Opaque from the solid side to <paramref name="from"/>, eased to nothing by
        /// <paramref name="to"/> - a smoothstep in five stops, so it has no visible start or end.
        /// </summary>
        private static CompositionLinearGradientBrush CreateRamp(Compositor compositor, float from, float to, bool fadesUp)
        {
            from = Math.Clamp(from, 0, 0.999f);
            to = Math.Clamp(to, from + 0.001f, 1);

            var ramp = compositor.CreateLinearGradientBrush();
            ramp.StartPoint = new Vector2(0, fadesUp ? 1 : 0);
            ramp.EndPoint = new Vector2(0, fadesUp ? 0 : 1);
            ramp.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(255, 255, 255, 255)));

            for (int i = 0; i <= 4; i++)
            {
                var t = i / 4f;
                var eased = 1 - t * t * (3 - 2 * t);
                ramp.ColorStops.Add(compositor.CreateColorGradientStop(from + (to - from) * t, Color.FromArgb((byte)(255 * eased), 255, 255, 255)));
            }

            if (to < 1)
            {
                ramp.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255)));
            }

            return ramp;
        }
    }
}
