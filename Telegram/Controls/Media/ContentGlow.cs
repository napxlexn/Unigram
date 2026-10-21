//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Microsoft.Graphics.Canvas.Effects;
using System.Numerics;
using Telegram.Common;
using Telegram.Navigation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Hosting;

namespace Telegram.Controls.Media
{
    /// <summary>
    /// The glow of a phosphor screen: what an element shows - its text, its pictures - bleeds
    /// softly into the glass around it, the way letters on an old tube monitor carry a halo.
    ///
    /// A live copy of the source element (a VisualSurface, so it follows every change) is
    /// blurred and laid over the source with a screen blend against what is already drawn:
    /// light content lights up its surroundings, dark content adds nothing, and nothing gets
    /// darker or blurrier - the crisp content stays crisp underneath. The host is an empty
    /// element placed over the source; it must not contain it, or the copy would contain
    /// itself.
    /// </summary>
    public static class ContentGlow
    {
        // How far past the source the halo may reach, so that it is not cut at the edge.
        private const float Bleed = 12;

        public static void Attach(FrameworkElement host, FrameworkElement source, bool scanlines)
        {
            Detach(host);

            if (!PowerSavingPolicy.AreMaterialsEnabled)
            {
                return;
            }

            var compositor = BootStrapper.Current.Compositor;

            var surface = compositor.CreateVisualSurface();
            surface.SourceVisual = ElementComposition.GetElementVisual(source);
            surface.SourceOffset = new Vector2(-Bleed);

            var copy = compositor.CreateSurfaceBrush(surface);
            copy.Stretch = CompositionStretch.None;
            copy.HorizontalAlignmentRatio = 0;
            copy.VerticalAlignmentRatio = 0;

            var blur = new GaussianBlurEffect
            {
                BlurAmount = scanlines ? 5 : 6,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("Content")
            };

            // The halo is the content's own colour, brightened: a plain blur at full strength
            // reads as smudge, at a fraction of it as nothing.
            var gain = scanlines ? 1.5f : 1.25f;
            var boost = new ColorMatrixEffect
            {
                ColorMatrix = new Matrix5x4
                {
                    M11 = gain, M22 = gain, M33 = gain, M44 = 0.9f,
                },
                Source = blur
            };

            var screen = new BlendEffect
            {
                Mode = BlendEffectMode.Screen,
                Background = new CompositionEffectSourceParameter("Backdrop"),
                Foreground = boost
            };

            var brush = compositor.CreateEffectFactory(screen).CreateBrush();
            brush.SetSourceParameter("Content", copy);
            brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());

            var glow = compositor.CreateSpriteVisual();
            glow.Brush = brush;
            glow.Offset = new Vector3(-Bleed, -Bleed, 0);

            var root = compositor.CreateContainerVisual();
            root.Children.InsertAtTop(glow);

            SpriteVisual lines = null;
            if (scanlines)
            {
                // A CRT's scanlines: a dark line every third pixel, faint enough to be felt
                // rather than seen.
                var gradient = compositor.CreateLinearGradientBrush();
                gradient.MappingMode = CompositionMappingMode.Absolute;
                gradient.ExtendMode = CompositionGradientExtendMode.Wrap;
                gradient.StartPoint = new Vector2(0, 0);
                gradient.EndPoint = new Vector2(0, 3);
                gradient.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(0, 0, 0, 0)));
                gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.6f, Color.FromArgb(0, 0, 0, 0)));
                gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.62f, Color.FromArgb(0x26, 0, 0, 0)));
                gradient.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0x26, 0, 0, 0)));

                lines = compositor.CreateSpriteVisual();
                lines.Brush = gradient;
                root.Children.InsertAtTop(lines);
            }

            void Resize(Vector2 size)
            {
                var padded = size + new Vector2(Bleed * 2);
                surface.SourceSize = padded;
                glow.Size = padded;

                if (lines != null)
                {
                    lines.Size = size;
                }
            }

            Resize(source.ActualSize);

            void OnSizeChanged(object sender, SizeChangedEventArgs args)
            {
                Resize(source.ActualSize);
            }

            source.SizeChanged += OnSizeChanged;

            ElementCompositionPreview.SetElementChildVisual(host, root);
            host.Tag = new Attachment(source, OnSizeChanged, root);
        }

        public static void Detach(FrameworkElement host)
        {
            if (host?.Tag is Attachment attachment)
            {
                attachment.Source.SizeChanged -= attachment.Handler;
                host.Tag = null;

                // Released rather than disposed: the compositor may still be drawing it.
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
        }

        private sealed record Attachment(FrameworkElement Source, SizeChangedEventHandler Handler, Visual Root);
    }
}
