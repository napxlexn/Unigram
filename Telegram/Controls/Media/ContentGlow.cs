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
using Telegram.Services;
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

        // Per compositor - a factory belongs to the one that made it - and per phosphor colour.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compositor, Dictionary<Color, CompositionEffectFactory>> _factories = new();

        /// <param name="phosphor">The glow's colour: transparent for each source's own, any other
        /// for a monochrome phosphor - green or amber - whatever the text is.</param>
        public static void Attach(FrameworkElement host, Func<IEnumerable<(FrameworkElement Element, float Strength)>> sources, Color phosphor)
        {
            Detach(host);

            var intensity = AppSettings.Appearance.GlowIntensity;
            if (!PowerSavingPolicy.AreMaterialsEnabled || intensity == 0)
            {
                return;
            }

            var root = BootStrapper.Current.Compositor.CreateContainerVisual();
            var attachment = new Attachment(host, sources, phosphor, intensity / 60f, root);

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

        private static CompositionEffectFactory GetFactory(Compositor compositor, Color phosphor)
        {
            var factories = _factories.GetValue(compositor, _ => new Dictionary<Color, CompositionEffectFactory>());
            if (factories.TryGetValue(phosphor, out var factory))
            {
                return factory;
            }

            // A tight halo: one that sits close around the letters reads as the screen glowing,
            // a wide one as the text smudging.
            var blur = new GaussianBlurEffect
            {
                BlurAmount = 4,
                BorderMode = EffectBorderMode.Soft,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("Content")
            };

            // The halo is the content's own colour, brightened - or, for a phosphor, its
            // brightness in the phosphor's colour.
            Matrix5x4 matrix;
            if (phosphor.A == 0)
            {
                matrix = new Matrix5x4 { M11 = 1.2f, M22 = 1.2f, M33 = 1.2f, M44 = 0.8f };
            }
            else
            {
                float r = phosphor.R / 255f * 1.3f, g = phosphor.G / 255f * 1.3f, b = phosphor.B / 255f * 1.3f;
                matrix = new Matrix5x4
                {
                    M11 = 0.3f * r, M21 = 0.59f * r, M31 = 0.11f * r,
                    M12 = 0.3f * g, M22 = 0.59f * g, M32 = 0.11f * g,
                    M13 = 0.3f * b, M23 = 0.59f * b, M33 = 0.11f * b,
                    M44 = 0.8f,
                };
            }

            var boost = new ColorMatrixEffect { ColorMatrix = matrix, Source = blur };

            var screen = new BlendEffect
            {
                Mode = BlendEffectMode.Screen,
                Background = new CompositionEffectSourceParameter("Backdrop"),
                Foreground = boost
            };

            return factories[phosphor] = compositor.CreateEffectFactory(screen);
        }

        private sealed class Attachment
        {
            private readonly FrameworkElement _host;
            private readonly Func<IEnumerable<(FrameworkElement Element, float Strength)>> _sources;
            private readonly Color _phosphor;
            private readonly float _intensity;
            private readonly ContainerVisual _root;

            private readonly Dictionary<FrameworkElement, Glow> _glows = new();

            public Attachment(FrameworkElement host, Func<IEnumerable<(FrameworkElement Element, float Strength)>> sources, Color phosphor, float intensity, ContainerVisual root)
            {
                _host = host;
                _sources = sources;
                _phosphor = phosphor;
                _intensity = intensity;
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
                        _glows[source] = glow = new Glow(source, _phosphor, _root, Refresh);
                    }

                    try
                    {
                        var origin = source.TransformToVisual(_host).TransformPoint(default);
                        glow.Place(new Vector2((float)origin.X, (float)origin.Y), source.ActualSize, Math.Min(1, strength * _intensity));
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

            public Glow(FrameworkElement source, Color phosphor, ContainerVisual root, Action refresh)
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

                var brush = GetFactory(compositor, phosphor).CreateBrush();
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
    }
}
