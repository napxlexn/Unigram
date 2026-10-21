//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using System;
using System.Numerics;
using Telegram.Common;
using Telegram.Navigation;
using Telegram.Services;
using Telegram.Services.Settings;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace Telegram.Controls.Media
{
    /// <summary>
    /// A pane of any <see cref="GlassKind"/>, with smooth corners: the blur and its tint, a wash
    /// of colour, light from inside, scanlines and the rim, each as <see cref="Glass.Resolve"/>
    /// and the intensity settings say.
    ///
    /// A composition brush set as the background of a rounded XAML element is cut to the corners
    /// without antialiasing, and a XAML border drawn over it is antialiased - so the glass showed
    /// in steps outside the rim at every corner. Here everything is Composition: the layers are
    /// masked by the element's outline drawn as a shape, which is antialiased, rendered once
    /// into a surface; the rim is the same outline stroked, half a pixel in so that a one pixel
    /// line lands on whole pixels. Each corner keeps its own radius, so a bubble's shape holds.
    ///
    /// The host is an empty Border behind the content; its CornerRadius is the glass's.
    /// </summary>
    public static class SmoothGlass
    {
        /// <param name="tint">The element's own colour, for kinds that take it.</param>
        /// <param name="fill">False to leave the fill to the element - a gradient bubble - and draw
        /// only what goes over it.</param>
        public static void Attach(Border host, GlassKind kind, bool dark, Color? tint = null, bool fill = true, bool rim = true)
        {
            Detach(host);

            var spec = Glass.Resolve(kind, dark);
            if (spec.IsOriginal)
            {
                return;
            }

            var attachment = new Attachment(BootStrapper.Current.Compositor, spec, dark, Glass.HasOwnTint(kind) ? spec.Tint : tint ?? spec.Tint, tint, fill, rim);

            host.Tag = attachment;
            host.SizeChanged += attachment.OnSizeChanged;
            attachment.Token = host.RegisterPropertyChangedCallback(Border.CornerRadiusProperty, attachment.OnCornerRadiusChanged);

            ElementCompositionPreview.SetElementChildVisual(host, attachment.Root);
            attachment.Update(host);
        }

        public static void Detach(Border host)
        {
            if (host?.Tag is Attachment attachment)
            {
                host.SizeChanged -= attachment.OnSizeChanged;
                host.UnregisterPropertyChangedCallback(Border.CornerRadiusProperty, attachment.Token);
                host.Tag = null;

                // Released rather than disposed: the compositor may still be drawing it.
                ElementCompositionPreview.SetElementChildVisual(host, null);
            }
        }

        /// <summary>
        /// An outline with a radius per corner, as a composition path.
        /// </summary>
        private static CompositionPath CreateOutline(Vector2 origin, Vector2 size, CornerRadius radius)
        {
            float Clamp(double value) => (float)Math.Max(0, Math.Min(value, Math.Min(size.X, size.Y) / 2));

            var tl = Clamp(radius.TopLeft);
            var tr = Clamp(radius.TopRight);
            var br = Clamp(radius.BottomRight);
            var bl = Clamp(radius.BottomLeft);

            float x = origin.X, y = origin.Y, w = size.X, h = size.Y;

            using var builder = new CanvasPathBuilder(CanvasDevice.GetSharedDevice());
            builder.BeginFigure(x + tl, y);
            builder.AddLine(x + w - tr, y);
            if (tr > 0) builder.AddArc(new Vector2(x + w, y + tr), tr, tr, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            builder.AddLine(x + w, y + h - br);
            if (br > 0) builder.AddArc(new Vector2(x + w - br, y + h), br, br, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            builder.AddLine(x + bl, y + h);
            if (bl > 0) builder.AddArc(new Vector2(x, y + h - bl), bl, bl, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            builder.AddLine(x, y + tl);
            if (tl > 0) builder.AddArc(new Vector2(x + tl, y), tl, tl, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
            builder.EndFigure(CanvasFigureLoop.Closed);

            return new CompositionPath(CanvasGeometry.CreatePath(builder));
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compositor, CompositionEffectFactory> _rimGlowFactories = new();

        private static Color Scale(Color color, double factor)
        {
            color.A = (byte)Math.Clamp(color.A * factor, 0, 255);
            return color;
        }

        private sealed class Attachment
        {
            public readonly ContainerVisual Root;
            public long Token;

            private readonly Compositor _compositor;

            private readonly CompositionPathGeometry _maskGeometry;
            private readonly ShapeVisual _maskShape;
            private readonly CompositionVisualSurface _maskSurface;
            private readonly CompositionSurfaceBrush _mask;

            private readonly SpriteVisual[] _layers;
            private readonly CompositionLinearGradientBrush[] _verticals;

            private readonly ShapeVisual _rim;
            private readonly CompositionPathGeometry[] _rimGeometries;
            private readonly float[] _rimInsets;

            private readonly SpriteVisual _rimGlow;
            private readonly CompositionVisualSurface _rimGlowSurface;

            public Attachment(Compositor compositor, GlassSpec spec, bool dark, Color? tint, Color? element, bool fill, bool rim)
            {
                _compositor = compositor;
                Root = compositor.CreateContainerVisual();

                _maskGeometry = compositor.CreatePathGeometry();

                var maskFill = compositor.CreateSpriteShape(_maskGeometry);
                maskFill.FillBrush = compositor.CreateColorBrush(Colors.White);

                _maskShape = compositor.CreateShapeVisual();
                _maskShape.Shapes.Add(maskFill);

                _maskSurface = compositor.CreateVisualSurface();
                _maskSurface.SourceVisual = _maskShape;
                _mask = compositor.CreateSurfaceBrush(_maskSurface);

                var appearance = AppSettings.Appearance;
                var layers = new System.Collections.Generic.List<SpriteVisual>();
                var verticals = new System.Collections.Generic.List<CompositionLinearGradientBrush>();

                // The pane itself.
                if (fill && tint is Color color)
                {
                    var blur = Glass.ScaleBlur(spec.Blur);
                    CompositionBrush source;

                    if (blur > 0 && PowerSavingPolicy.AreMaterialsEnabled)
                    {
                        source = SolidGaussianBrush.CreateBrush(compositor, color, blur, spec.Saturation);
                    }
                    else
                    {
                        var opaque = color;
                        opaque.A = blur > 0 ? (byte)Math.Max(color.A, (byte)0xF0) : color.A;
                        source = compositor.CreateColorBrush(opaque);
                    }

                    layers.Add(CreateMasked(source));
                }

                // A wash of colour across it, corner to corner.
                if (spec.Wash is Color[] wash && wash.Length > 1)
                {
                    var gradient = compositor.CreateLinearGradientBrush();
                    gradient.StartPoint = Vector2.Zero;
                    gradient.EndPoint = Vector2.One;

                    for (int i = 0; i < wash.Length; i++)
                    {
                        gradient.ColorStops.Add(compositor.CreateColorGradientStop(i / (float)(wash.Length - 1), wash[i]));
                    }

                    layers.Add(CreateMasked(gradient));
                }

                // Light from inside: a lamp behind the glass, brightest in the middle.
                if (spec.InnerLight is Color light && appearance.GlowIntensity > 0)
                {
                    var radial = compositor.CreateRadialGradientBrush();
                    radial.EllipseCenter = new Vector2(0.5f, 0.55f);
                    radial.EllipseRadius = new Vector2(0.75f, 0.9f);
                    radial.ColorStops.Add(compositor.CreateColorGradientStop(0, Scale(light, 0.55 * appearance.GlowIntensity / 100)));
                    radial.ColorStops.Add(compositor.CreateColorGradientStop(1, Scale(light, 0)));

                    layers.Add(CreateMasked(radial));
                }

                // A CRT's scanlines: a dark line every third pixel.
                if (spec.Scanlines && appearance.ScanlineIntensity > 0)
                {
                    var alpha = (byte)(0x40 * appearance.ScanlineIntensity / 100);

                    var lines = compositor.CreateLinearGradientBrush();
                    lines.MappingMode = CompositionMappingMode.Absolute;
                    lines.ExtendMode = CompositionGradientExtendMode.Wrap;
                    lines.StartPoint = Vector2.Zero;
                    lines.EndPoint = new Vector2(0, 3);
                    lines.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(0, 0, 0, 0)));
                    lines.ColorStops.Add(compositor.CreateColorGradientStop(0.62f, Color.FromArgb(0, 0, 0, 0)));
                    lines.ColorStops.Add(compositor.CreateColorGradientStop(0.66f, Color.FromArgb(alpha, 0, 0, 0)));
                    lines.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(alpha, 0, 0, 0)));

                    layers.Add(CreateMasked(lines));
                }

                _layers = layers.ToArray();

                foreach (var layer in _layers)
                {
                    Root.Children.InsertAtTop(layer);
                }

                // The rim: the style setting, unless the kind brings its own.
                var strength = appearance.RimIntensity / 60d;
                var style = spec.RimColor != null && appearance.RimStyle is RimStyle.None or RimStyle.Thin or RimStyle.Soft
                    ? RimStyle.Thin
                    : appearance.RimStyle;

                if (!rim || strength <= 0 || (style == RimStyle.None && spec.RimColor == null))
                {
                    _rimGeometries = Array.Empty<CompositionPathGeometry>();
                    _rimInsets = Array.Empty<float>();
                    _verticals = Array.Empty<CompositionLinearGradientBrush>();
                    return;
                }

                var top = spec.RimColor ?? (dark ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
                var bottom = spec.RimColor is Color own
                    ? Scale(own, 0.35)
                    : dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00);

                // A colour of the user's choosing instead, as bright as the kinds' own rims.
                if (Glass.GetRimColor(element, dark) is Color chosen)
                {
                    top = chosen;
                    bottom = Scale(chosen, 0.35);
                }

                var width = appearance.RimWidth / 100f;
                _light = appearance.RimLight;

                top = Scale(top, strength);
                bottom = Scale(bottom, strength);

                _rim = compositor.CreateShapeVisual();

                var geometries = new System.Collections.Generic.List<CompositionPathGeometry>();
                var insets = new System.Collections.Generic.List<float>();

                // The light along the rim: as the style has it, or as the setting turns it.
                CompositionBrush Vertical(params (float Offset, Color Color)[] stops)
                {
                    if (_light == RimLight.Even)
                    {
                        return compositor.CreateColorBrush(stops[0].Color);
                    }

                    var brush = compositor.CreateLinearGradientBrush();
                    brush.MappingMode = CompositionMappingMode.Absolute;

                    foreach (var (offset, stop) in stops)
                    {
                        brush.ColorStops.Add(compositor.CreateColorGradientStop(offset, stop));
                    }

                    verticals.Add(brush);
                    return brush;
                }

                // A solid colour, lit along the rim unless the style keeps it even.
                CompositionBrush Lit(Color color)
                {
                    return _light is RimLight.Style or RimLight.Even
                        ? compositor.CreateColorBrush(color)
                        : Vertical((0, color), (1, Scale(color, 0.3)));
                }

                void Stroke(CompositionBrush brush, float thickness, float inset)
                {
                    thickness *= width;
                    inset *= width;

                    var geometry = compositor.CreatePathGeometry();
                    var shape = compositor.CreateSpriteShape(geometry);
                    shape.StrokeBrush = brush;
                    shape.StrokeThickness = thickness;

                    _rim.Shapes.Add(shape);
                    geometries.Add(geometry);
                    insets.Add(inset);
                }

                switch (style)
                {
                    case RimStyle.Soft:
                        Stroke(Lit(Scale(top, 0.5)), 1, 0.5f);
                        break;

                    case RimStyle.TopLight:
                        Stroke(Vertical((0, top), (0.45f, Scale(top, 0))), 1, 0.5f);
                        break;

                    case RimStyle.Double:
                        // A dark edge outside, a light one just inside it: thick glass.
                        Stroke(compositor.CreateColorBrush(Scale(Color.FromArgb(0x50, 0, 0, 0), strength)), 1, 0.5f);
                        Stroke(Vertical((0, top), (1, bottom)), 1, 1.5f);
                        break;

                    case RimStyle.Bold:
                        Stroke(Vertical((0, Scale(top, 1.4)), (1, Scale(bottom, 1.4))), 1.5f, 0.75f);
                        break;

                    case RimStyle.Iridescent:
                        {
                            var rainbow = compositor.CreateLinearGradientBrush();
                            rainbow.StartPoint = Vector2.Zero;
                            rainbow.EndPoint = new Vector2(1, 0.4f);
                            var a = top.A;
                            rainbow.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(a, 0xFF, 0x9C, 0xE0)));
                            rainbow.ColorStops.Add(compositor.CreateColorGradientStop(0.35f, Color.FromArgb(a, 0x9C, 0xD8, 0xFF)));
                            rainbow.ColorStops.Add(compositor.CreateColorGradientStop(0.7f, Color.FromArgb(a, 0xC8, 0xFF, 0xB0)));
                            rainbow.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(a, 0xFF, 0xE0, 0x9C)));
                            Stroke(rainbow, 1, 0.5f);
                        }
                        break;

                    default:
                        Stroke(Vertical((0, top), (1, bottom)), 1, 0.5f);
                        break;
                }

                // A glint running along it: the outline again, lit only in a narrow band that
                // Update slides across.
                if (appearance.RimAnimation == RimAnimation.Shimmer)
                {
                    var glint = Scale(Colors.White, Math.Min(1, 0.9 * strength));
                    var clear = Scale(glint, 0);

                    _shimmer = compositor.CreateLinearGradientBrush();
                    _shimmer.MappingMode = CompositionMappingMode.Absolute;
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.38f, clear));
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, glint));
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.62f, clear));

                    Stroke(_shimmer, 1.5f, 0.75f);
                }

                _rimGeometries = geometries.ToArray();
                _rimInsets = insets.ToArray();
                _verticals = verticals.ToArray();

                // A rim that glows: the outline again, wider and blurred, light spilling both ways
                // from the edge.
                if (style == RimStyle.Glowing || spec.RimGlows)
                {
                    var glowColor = Scale(spec.RimColor ?? top, spec.RimGlows ? 1.6 : 1.1);

                    var glowShape = compositor.CreateShapeVisual();
                    var glowGeometry = compositor.CreatePathGeometry();
                    var glowStroke = compositor.CreateSpriteShape(glowGeometry);
                    glowStroke.StrokeBrush = compositor.CreateColorBrush(glowColor);
                    glowStroke.StrokeThickness = 3 * width;
                    glowShape.Shapes.Add(glowStroke);

                    _rimGlowSurface = compositor.CreateVisualSurface();
                    _rimGlowSurface.SourceVisual = glowShape;

                    if (!_rimGlowFactories.TryGetValue(compositor, out var factory))
                    {
                        var blurred = new GaussianBlurEffect
                        {
                            BlurAmount = 4,
                            BorderMode = EffectBorderMode.Soft,
                            Source = new CompositionEffectSourceParameter("Rim")
                        };

                        factory = compositor.CreateEffectFactory(blurred);
                        _rimGlowFactories.Add(compositor, factory);
                    }

                    var brush = factory.CreateBrush();
                    brush.SetSourceParameter("Rim", compositor.CreateSurfaceBrush(_rimGlowSurface));

                    _rimGlow = compositor.CreateSpriteVisual();
                    _rimGlow.Brush = brush;
                    _rimGlowShape = glowShape;
                    _rimGlowGeometry = glowGeometry;

                    Root.Children.InsertAtTop(_rimGlow);
                }

                Root.Children.InsertAtTop(_rim);

                // Breathing: the rim, and its glow, dim and come back.
                if (appearance.RimAnimation == RimAnimation.Pulse)
                {
                    var pulse = compositor.CreateScalarKeyFrameAnimation();
                    pulse.InsertKeyFrame(0, 1);
                    pulse.InsertKeyFrame(0.5f, 0.3f);
                    pulse.InsertKeyFrame(1, 1);
                    pulse.Duration = TimeSpan.FromSeconds(2.6);
                    pulse.IterationBehavior = AnimationIterationBehavior.Forever;

                    _rim.StartAnimation("Opacity", pulse);
                    _rimGlow?.StartAnimation("Opacity", pulse);
                }
            }

            private readonly RimLight _light;
            private readonly CompositionLinearGradientBrush _shimmer;
            private Vector2 _shimmerSize;

            private readonly ShapeVisual _rimGlowShape;
            private readonly CompositionPathGeometry _rimGlowGeometry;

            // How far the glowing rim may spill past the edge.
            private const float Spill = 8;

            private SpriteVisual CreateMasked(CompositionBrush source)
            {
                var masked = _compositor.CreateMaskBrush();
                masked.Source = source;
                masked.Mask = _mask;

                var sprite = _compositor.CreateSpriteVisual();
                sprite.Brush = masked;
                return sprite;
            }

            public void OnSizeChanged(object sender, SizeChangedEventArgs e)
            {
                Update(sender as Border);
            }

            public void OnCornerRadiusChanged(DependencyObject sender, DependencyProperty dp)
            {
                Update(sender as Border);
            }

            public void Update(Border host)
            {
                if (host == null)
                {
                    return;
                }

                var size = host.ActualSize;
                if (size.X < 1 || size.Y < 1)
                {
                    return;
                }

                var radius = host.CornerRadius;

                _maskGeometry.Path = CreateOutline(Vector2.Zero, size, radius);
                _maskShape.Size = size;
                _maskSurface.SourceSize = size;

                foreach (var layer in _layers)
                {
                    layer.Size = size;
                }

                if (_rim == null)
                {
                    return;
                }

                _rim.Size = size;

                for (int i = 0; i < _rimGeometries.Length; i++)
                {
                    var inset = _rimInsets[i];
                    var inner = new CornerRadius(
                        Math.Max(0, radius.TopLeft - inset), Math.Max(0, radius.TopRight - inset),
                        Math.Max(0, radius.BottomRight - inset), Math.Max(0, radius.BottomLeft - inset));

                    _rimGeometries[i].Path = CreateOutline(new Vector2(inset), Vector2.Max(size - new Vector2(inset * 2), Vector2.Zero), inner);
                }

                foreach (var vertical in _verticals)
                {
                    vertical.StartPoint = _light == RimLight.BottomUp ? new Vector2(0, size.Y) : Vector2.Zero;
                    vertical.EndPoint = _light switch
                    {
                        RimLight.BottomUp => Vector2.Zero,
                        RimLight.Diagonal => size,
                        _ => new Vector2(0, size.Y)
                    };
                }

                // The band spans the element corner to corner and slides from wholly before it to
                // wholly past it, then rests; restarted for a new size.
                if (_shimmer != null && size != _shimmerSize)
                {
                    _shimmerSize = size;

                    Vector2KeyFrameAnimation Run(Vector2 from)
                    {
                        var run = _compositor.CreateVector2KeyFrameAnimation();
                        run.InsertKeyFrame(0, from - size);
                        run.InsertKeyFrame(0.6f, from + size);
                        run.InsertKeyFrame(1, from + size);
                        run.Duration = TimeSpan.FromSeconds(3.2);
                        run.IterationBehavior = AnimationIterationBehavior.Forever;
                        return run;
                    }

                    // Both ends together, so the band keeps its width on the way.
                    _shimmer.StartAnimation("StartPoint", Run(Vector2.Zero));
                    _shimmer.StartAnimation("EndPoint", Run(size));
                }

                if (_rimGlow != null)
                {
                    var padded = size + new Vector2(Spill * 2);

                    _rimGlowGeometry.Path = CreateOutline(new Vector2(Spill + 1), Vector2.Max(size - new Vector2(2), Vector2.Zero), radius);
                    _rimGlowShape.Size = padded;
                    _rimGlowSurface.SourceSize = padded;
                    _rimGlow.Size = padded;
                    _rimGlow.Offset = new Vector3(-Spill, -Spill, 0);
                }
            }
        }
    }
}
