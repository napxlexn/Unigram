//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
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
    /// The glow of a phosphor screen: text, reactions and the like bleed softly into the glass
    /// around them, the way letters on an old tube monitor carry a halo.
    ///
    /// Each source element gets a live copy of itself (a VisualSurface, so it follows every
    /// change), blurred and laid over it with a screen blend against what is already drawn:
    /// light content lights up its surroundings, dark content adds nothing, and nothing gets
    /// darker or blurrier - the crisp content stays crisp underneath. Only the sources glow: a
    /// post's photo or video is never one of them, so its picture stays exactly as it is.
    ///
    /// The host is an empty element over the sources; it must not contain them, or a copy would
    /// contain itself.
    /// </summary>
    public static class ContentGlow
    {
        // How far past a source the halo may reach, so that it is not cut at the edge.
        private const float Bleed = 10;

        private static CompositionEffectFactory _glowFactory;
        private static CompositionEffectFactory _terminalFactory;

        public static void Attach(FrameworkElement host, Func<IEnumerable<(FrameworkElement Element, float Strength)>> sources, bool terminal)
        {
            Detach(host);

            if (!PowerSavingPolicy.AreMaterialsEnabled)
            {
                return;
            }

            var root = BootStrapper.Current.Compositor.CreateContainerVisual();
            var attachment = new Attachment(host, sources, terminal, root);

            host.SizeChanged += attachment.OnSizeChanged;
            host.Tag = attachment;

            ElementCompositionPreview.SetElementChildVisual(host, root);
            attachment.Refresh();
        }

        public static void Detach(FrameworkElement host)
        {
            if (host?.Tag is Attachment attachment)
            {
                attachment.Close();
                host.Tag = null;

                // Released rather than disposed: the compositor may still be drawing it.
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
        }

        private static CompositionEffectFactory GetFactory(Compositor compositor, bool terminal)
        {
            ref var factory = ref terminal ? ref _terminalFactory : ref _glowFactory;
            if (factory != null)
            {
                return factory;
            }

            // The terminal's is the tighter, fainter one: a phosphor halo that sits close around
            // the letters, so that it reads as the screen glowing rather than the text smudging.
            var blur = new GaussianBlurEffect
            {
                BlurAmount = terminal ? 3.5f : 6,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("Content")
            };

            // The halo is the content's own colour, brightened: a plain blur at full strength
            // reads as smudge, at a fraction of it as nothing.
            var gain = terminal ? 1.1f : 1.3f;
            var alpha = terminal ? 0.55f : 0.9f;
            var boost = new ColorMatrixEffect
            {
                ColorMatrix = new Matrix5x4
                {
                    M11 = gain, M22 = gain, M33 = gain, M44 = alpha,
                },
                Source = blur
            };

            var screen = new BlendEffect
            {
                Mode = BlendEffectMode.Screen,
                Background = new CompositionEffectSourceParameter("Backdrop"),
                Foreground = boost
            };

            return factory = compositor.CreateEffectFactory(screen);
        }

        private sealed class Attachment
        {
            private readonly FrameworkElement _host;
            private readonly Func<IEnumerable<(FrameworkElement Element, float Strength)>> _sources;
            private readonly bool _terminal;
            private readonly ContainerVisual _root;

            private readonly Dictionary<FrameworkElement, Glow> _glows = new();

            public Attachment(FrameworkElement host, Func<IEnumerable<(FrameworkElement Element, float Strength)>> sources, bool terminal, ContainerVisual root)
            {
                _host = host;
                _sources = sources;
                _terminal = terminal;
                _root = root;
            }

            public void OnSizeChanged(object sender, SizeChangedEventArgs e)
            {
                Refresh();
            }

            /// <summary>
            /// Matches the glows to the sources there are now - a reaction row is loaded late, a
            /// caption comes and goes with an edit - and puts each over its source.
            /// </summary>
            public void Refresh()
            {
                var current = new HashSet<FrameworkElement>();

                foreach (var (source, strength) in _sources())
                {
                    if (source == null || source.Visibility != Visibility.Visible || source.ActualWidth < 1 || source.ActualHeight < 1)
                    {
                        continue;
                    }

                    current.Add(source);

                    if (!_glows.TryGetValue(source, out var glow))
                    {
                        _glows[source] = glow = new Glow(source, _terminal, _root, Refresh);
                    }

                    try
                    {
                        var origin = source.TransformToVisual(_host).TransformPoint(default);
                        glow.Place(new Vector2((float)origin.X, (float)origin.Y), source.ActualSize, strength);
                    }
                    catch
                    {
                        // Not in the same tree any more; dropped on the next pass.
                    }
                }

                foreach (var stale in new List<FrameworkElement>(_glows.Keys))
                {
                    if (!current.Contains(stale))
                    {
                        _glows[stale].Remove(_root);
                        _glows.Remove(stale);
                    }
                }
            }

            public void Close()
            {
                _host.SizeChanged -= OnSizeChanged;

                foreach (var glow in _glows.Values)
                {
                    glow.Remove(_root);
                }

                _glows.Clear();
            }
        }

        private sealed class Glow
        {
            private readonly FrameworkElement _source;
            private readonly SizeChangedEventHandler _handler;

            private readonly CompositionVisualSurface _surface;
            private readonly SpriteVisual _glow;

            public Glow(FrameworkElement source, bool terminal, ContainerVisual root, Action refresh)
            {
                var compositor = root.Compositor;

                _source = source;
                _handler = (s, e) => refresh();
                _source.SizeChanged += _handler;

                _surface = compositor.CreateVisualSurface();
                _surface.SourceVisual = ElementComposition.GetElementVisual(source);
                _surface.SourceOffset = new Vector2(-Bleed);

                var copy = compositor.CreateSurfaceBrush(_surface);
                copy.Stretch = CompositionStretch.None;
                copy.HorizontalAlignmentRatio = 0;
                copy.VerticalAlignmentRatio = 0;

                var brush = GetFactory(compositor, terminal).CreateBrush();
                brush.SetSourceParameter("Content", copy);
                brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());

                _glow = compositor.CreateSpriteVisual();
                _glow.Brush = brush;
                root.Children.InsertAtTop(_glow);
            }

            public void Place(Vector2 origin, Vector2 size, float strength)
            {
                var padded = size + new Vector2(Bleed * 2);

                _surface.SourceSize = padded;
                _glow.Size = padded;
                _glow.Offset = new Vector3(origin - new Vector2(Bleed), 0);
                _glow.Opacity = strength;
            }

            public void Remove(ContainerVisual root)
            {
                _source.SizeChanged -= _handler;
                root.Children.Remove(_glow);
            }
        }

        #region Scanlines

        /// <summary>
        /// A CRT's scanlines, for the terminal style: a faint dark line every third pixel across
        /// the host. The host goes behind the content, over the bubble's fill only - lines drawn
        /// through the letters themselves cost more legibility than they bring atmosphere.
        /// </summary>
        public static void AttachScanlines(FrameworkElement host)
        {
            DetachScanlines(host);

            var compositor = BootStrapper.Current.Compositor;

            var gradient = compositor.CreateLinearGradientBrush();
            gradient.MappingMode = CompositionMappingMode.Absolute;
            gradient.ExtendMode = CompositionGradientExtendMode.Wrap;
            gradient.StartPoint = new Vector2(0, 0);
            gradient.EndPoint = new Vector2(0, 3);
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(0, 0, 0, 0)));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.62f, Color.FromArgb(0, 0, 0, 0)));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(0.66f, Color.FromArgb(0x1C, 0, 0, 0)));
            gradient.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0x1C, 0, 0, 0)));

            var lines = compositor.CreateSpriteVisual();
            lines.Brush = gradient;
            lines.Size = host.ActualSize;

            void OnSizeChanged(object sender, SizeChangedEventArgs args)
            {
                lines.Size = host.ActualSize;
            }

            host.SizeChanged += OnSizeChanged;
            host.Tag = new Scanlines(OnSizeChanged);

            ElementCompositionPreview.SetElementChildVisual(host, lines);
        }

        public static void DetachScanlines(FrameworkElement host)
        {
            if (host?.Tag is Scanlines scanlines)
            {
                host.SizeChanged -= scanlines.Handler;
                host.Tag = null;

                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
        }

        private sealed record Scanlines(SizeChangedEventHandler Handler);

        #endregion
    }
}
