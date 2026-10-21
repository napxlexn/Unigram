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
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;

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
        /// <param name="bubble">A message bubble, which may have a rim of its own.</param>
        public static void Attach(Border host, GlassKind kind, bool dark, Color? tint = null, bool fill = true, bool rim = true, bool bubble = false)
        {
            Detach(host);

            var spec = Glass.Resolve(kind, dark);
            if (spec.IsOriginal)
            {
                return;
            }

            var attachment = new Attachment(BootStrapper.Current.Compositor, spec, dark, Glass.HasOwnTint(kind) ? spec.Tint : tint ?? spec.Tint, tint, fill, rim, AppSettings.Appearance.RimFor(bubble));

            host.Tag = attachment;
            host.SizeChanged += attachment.OnSizeChanged;
            attachment.Token = host.RegisterPropertyChangedCallback(Border.CornerRadiusProperty, attachment.OnCornerRadiusChanged);

            ElementCompositionPreview.SetElementChildVisual(host, attachment.Root);
            attachment.Track(host);
            attachment.Update(host);
        }

        public static void Detach(Border host)
        {
            if (host?.Tag is Attachment attachment)
            {
                host.SizeChanged -= attachment.OnSizeChanged;
                host.UnregisterPropertyChangedCallback(Border.CornerRadiusProperty, attachment.Token);
                host.Tag = null;

                attachment.Untrack();

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

            public Attachment(Compositor compositor, GlassSpec spec, bool dark, Color? tint, Color? element, bool fill, bool rim, RimProfile profile)
            {
                _compositor = compositor;
                Root = compositor.CreateContainerVisual();

                var appearance = AppSettings.Appearance;

                // What the animations and the pointer drive: how far the pointer is over the
                // element, where the element sits in what tracks the pointer, the angle of the
                // circling light, the middle and the reach of the element.
                _state = compositor.CreatePropertySet();
                _state.InsertScalar("Hover", 0);
                _state.InsertVector2("Origin", Vector2.Zero);
                _state.InsertScalar("Angle", 0);
                _state.InsertVector2("Center", Vector2.Zero);
                _state.InsertScalar("Reach", 0);

                // How strongly the animations and the pointer effects show, 0 to 1.
                _state.InsertScalar("Power", profile.HoverIntensity / 100f);
                _motion = profile.AnimationIntensity / 100d;

                _hover = profile.Hover;
                _ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.37f, 0), new Vector2(0.63f, 1));

                _maskGeometry = compositor.CreatePathGeometry();

                var maskFill = compositor.CreateSpriteShape(_maskGeometry);
                maskFill.FillBrush = compositor.CreateColorBrush(Colors.White);

                _maskShape = compositor.CreateShapeVisual();
                _maskShape.Shapes.Add(maskFill);

                _maskSurface = compositor.CreateVisualSurface();
                _maskSurface.SourceVisual = _maskShape;
                _mask = compositor.CreateSurfaceBrush(_maskSurface);

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

                // A highlight on the glass where the pointer is, faded in while it is over.
                if (_hover is RimHover.Sheen or RimHover.All)
                {
                    var sheen = compositor.CreateRadialGradientBrush();
                    sheen.MappingMode = CompositionMappingMode.Absolute;
                    sheen.EllipseRadius = new Vector2(120);
                    sheen.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(dark ? (byte)0x40 : (byte)0x80, 0xFF, 0xFF, 0xFF)));
                    sheen.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 0xFF, 0xFF, 0xFF)));

                    FollowPointer(sheen);

                    var layer = CreateMasked(sheen);
                    var opacity = compositor.CreateExpressionAnimation("s.Hover * s.Power");
                    opacity.SetReferenceParameter("s", _state);
                    layer.StartAnimation("Opacity", opacity);

                    layers.Add(layer);
                }

                _layers = layers.ToArray();

                foreach (var layer in _layers)
                {
                    Root.Children.InsertAtTop(layer);
                }

                // The rim: the style setting, unless the kind brings its own.
                var strength = profile.Intensity / 60d;
                var style = spec.RimColor != null && profile.Style is RimStyle.None or RimStyle.Thin or RimStyle.Soft
                    ? RimStyle.Thin
                    : profile.Style;

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
                if (Glass.GetRimColor(element, dark, profile) is Color chosen)
                {
                    top = chosen;
                    bottom = Scale(chosen, 0.35);
                }

                var width = profile.Width / 100f;
                _light = profile.Light;

                top = Scale(top, strength);
                bottom = Scale(bottom, strength);

                _rim = compositor.CreateShapeVisual();

                var geometries = new System.Collections.Generic.List<CompositionPathGeometry>();
                var insets = new System.Collections.Generic.List<float>();
                var shapes = new System.Collections.Generic.List<CompositionSpriteShape>();
                var thicknesses = new System.Collections.Generic.List<float>();

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
                    shapes.Add(shape);
                    thicknesses.Add(thickness);
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
                var glint = Scale(Colors.White, Math.Min(1, 0.9 * strength) * _motion);

                if (profile.Animation == RimAnimation.Shimmer)
                {
                    // Soft at both sides of the band, so it never shows an edge as it moves.
                    _shimmer = compositor.CreateLinearGradientBrush();
                    _shimmer.MappingMode = CompositionMappingMode.Absolute;
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.3f, Scale(glint, 0)));
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.42f, Scale(glint, 0.35)));
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, glint));
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.58f, Scale(glint, 0.35)));
                    _shimmer.ColorStops.Add(compositor.CreateColorGradientStop(0.7f, Scale(glint, 0)));

                    Stroke(_shimmer, 1.5f, 0.75f);
                }
                else if (profile.Animation == RimAnimation.Rotate)
                {
                    // Lit from one side, the light fading across; the side turns round the middle.
                    var rotor = compositor.CreateLinearGradientBrush();
                    rotor.MappingMode = CompositionMappingMode.Absolute;
                    rotor.ColorStops.Add(compositor.CreateColorGradientStop(0, glint));
                    rotor.ColorStops.Add(compositor.CreateColorGradientStop(0.2f, Scale(glint, 0.5)));
                    rotor.ColorStops.Add(compositor.CreateColorGradientStop(0.5f, Scale(glint, 0)));

                    var start = compositor.CreateExpressionAnimation("Vector2(s.Center.X - s.Reach * Cos(s.Angle), s.Center.Y - s.Reach * Sin(s.Angle))");
                    start.SetReferenceParameter("s", _state);
                    var end = compositor.CreateExpressionAnimation("Vector2(s.Center.X + s.Reach * Cos(s.Angle), s.Center.Y + s.Reach * Sin(s.Angle))");
                    end.SetReferenceParameter("s", _state);
                    rotor.StartAnimation("StartPoint", start);
                    rotor.StartAnimation("EndPoint", end);

                    var turn = compositor.CreateScalarKeyFrameAnimation();
                    turn.InsertKeyFrame(0, 0);
                    turn.InsertKeyFrame(1, MathF.PI * 2, compositor.CreateLinearEasingFunction());
                    turn.Duration = TimeSpan.FromSeconds(5);
                    turn.IterationBehavior = AnimationIterationBehavior.Forever;
                    _state.StartAnimation("Angle", turn);

                    Stroke(rotor, 1.5f, 0.75f);
                }
                else if (profile.Animation == RimAnimation.Rainbow)
                {
                    // Corner to corner, each stop going round the colours a step behind the last.
                    var hues = new[]
                    {
                        Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B), Color.FromArgb(0xFF, 0xFF, 0xC8, 0x5A),
                        Color.FromArgb(0xFF, 0x7C, 0xF0, 0x6B), Color.FromArgb(0xFF, 0x5A, 0xD0, 0xFF),
                        Color.FromArgb(0xFF, 0x9A, 0x7C, 0xFF), Color.FromArgb(0xFF, 0xFF, 0x6B, 0xD8)
                    };

                    var alpha = Math.Min(1, 0.85 * strength) * _motion;

                    _rainbow = compositor.CreateLinearGradientBrush();
                    _rainbow.MappingMode = CompositionMappingMode.Absolute;

                    for (int i = 0; i < 3; i++)
                    {
                        var stop = compositor.CreateColorGradientStop(i / 2f, Scale(hues[i * 2], alpha));
                        var flow = compositor.CreateColorKeyFrameAnimation();
                        flow.InterpolationColorSpace = CompositionColorSpace.Hsl;

                        for (int j = 0; j <= hues.Length; j++)
                        {
                            flow.InsertKeyFrame(j / (float)hues.Length, Scale(hues[(i * 2 + j) % hues.Length], alpha), _ease);
                        }

                        flow.Duration = TimeSpan.FromSeconds(7);
                        flow.IterationBehavior = AnimationIterationBehavior.Forever;
                        stop.StartAnimation("Color", flow);

                        _rainbow.ColorStops.Add(stop);
                    }

                    Stroke(_rainbow, 1.5f, 0.75f);
                }

                // The rim lit up around the pointer: a light that grows in as the pointer comes
                // over the element and shrinks away as it leaves.
                if (_hover is RimHover.Spotlight or RimHover.All)
                {
                    var spot = compositor.CreateRadialGradientBrush();
                    spot.MappingMode = CompositionMappingMode.Absolute;
                    spot.ColorStops.Add(compositor.CreateColorGradientStop(0, Scale(Scale(top, Math.Max(1, 2.2 / Math.Max(0.1, top.A / 255d))), Math.Max(0.15, profile.HoverIntensity / 100d))));
                    spot.ColorStops.Add(compositor.CreateColorGradientStop(1, Scale(top, 0)));

                    FollowPointer(spot);

                    // Stronger: brighter, and reaching further along the rim.
                    var radius = compositor.CreateExpressionAnimation("Vector2(1 + (50 + 110 * s.Power) * s.Hover, 1 + (50 + 110 * s.Power) * s.Hover)");
                    radius.SetReferenceParameter("s", _state);
                    spot.StartAnimation("EllipseRadius", radius);

                    Stroke(spot, 2, 1);
                }

                _rimGeometries = geometries.ToArray();
                _rimInsets = insets.ToArray();
                _rimDrawnInsets = insets.ToArray();
                _rimShapes = shapes.ToArray();
                _rimThicknesses = thicknesses.ToArray();
                _verticals = verticals.ToArray();

                // A rim that glows: the outline again, wider and blurred, light spilling both ways
                // from the edge.
                var glows = style == RimStyle.Glowing || spec.RimGlows;
                var hoverGlow = _hover is RimHover.Glow or RimHover.All;

                if (glows || hoverGlow)
                {
                    var glowColor = Scale(spec.RimColor ?? top, (spec.RimGlows ? 1.6 : hoverGlow && !glows ? 2.2 : 1.1) * appearance.RimGlowIntensity / 60);

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

                    // Under the pointer: a glowing rim at full strength, any other only then.
                    if (hoverGlow)
                    {
                        var opacity = compositor.CreateExpressionAnimation(glows ? "0.55 + 0.45 * s.Hover * s.Power" : "s.Hover * s.Power");
                        opacity.SetReferenceParameter("s", _state);
                        _rimGlow.StartAnimation("Opacity", opacity);
                    }
                }

                _smoothing = profile.Smoothing;
                _rimDisplay = PresentRim(compositor);

                // The ones that change the rim's brightness: eased between every step, so that
                // nothing jumps; the glow follows unless the pointer has it.
                if (CreateBrightness(compositor, profile.Animation) is ScalarKeyFrameAnimation brightness)
                {
                    _rimDisplay.StartAnimation("Opacity", brightness);

                    if (!hoverGlow)
                    {
                        _rimGlow?.StartAnimation("Opacity", brightness);
                    }
                }
            }

            private readonly RimSmoothing _smoothing;
            private readonly Visual _rimDisplay;
            private readonly float[] _rimDrawnInsets;
            private readonly CompositionSpriteShape[] _rimShapes;
            private readonly float[] _rimThicknesses;

            // The rim drawn into a surface first, for the smoothing that works on its pixels.
            private ContainerVisual _rimHolder;
            private CompositionVisualSurface _rimSurface;
            private SpriteVisual _rimSprite;
            private ShapeVisual _rimHalo;

            // How far a softened rim may spread past the edge.
            private const float Feather = 2;

            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Compositor, CompositionEffectFactory> _featherFactories = new();

            /// <summary>
            /// Puts the rim into the glass, smoothed as the setting asks, and returns the visual
            /// that shows it.
            /// </summary>
            private Visual PresentRim(Compositor compositor)
            {
                switch (_smoothing)
                {
                    case RimSmoothing.Blend:
                        {
                            // The crisp line, and under it a blurred copy of it, mirrored from
                            // the live one: the curves get a soft edge, the line stays sharp.
                            Root.Children.InsertAtTop(_rim);

                            _rimSurface = compositor.CreateVisualSurface();
                            _rimSurface.SourceVisual = _rim;
                            _rimSurface.SourceOffset = new Vector2(-Feather);

                            var brush = CreateFeather(compositor, 1.1f);
                            brush.SetSourceParameter("Rim", compositor.CreateSurfaceBrush(_rimSurface));

                            _rimSprite = compositor.CreateSpriteVisual();
                            _rimSprite.Brush = brush;
                            _rimSprite.Opacity = 0.7f;
                            _rimSprite.Offset = new Vector3(-Feather, -Feather, 0);
                            Root.Children.InsertBelow(_rimSprite, _rim);

                            // Both brighten and dim together under the animations.
                            return _rim;
                        }

                    case RimSmoothing.Soft:
                        {
                            // A blur of under a pixel: the steps along the curves melt, the line
                            // stays a line.
                            _rim.Offset = new Vector3(Feather, Feather, 0);
                            _rimHolder = compositor.CreateContainerVisual();
                            _rimHolder.Children.InsertAtTop(_rim);

                            _rimSurface = compositor.CreateVisualSurface();
                            _rimSurface.SourceVisual = _rimHolder;

                            var brush = CreateFeather(compositor, 0.7f);
                            brush.SetSourceParameter("Rim", compositor.CreateSurfaceBrush(_rimSurface));

                            _rimSprite = compositor.CreateSpriteVisual();
                            _rimSprite.Brush = brush;
                            _rimSprite.Offset = new Vector3(-Feather, -Feather, 0);
                            Root.Children.InsertAtTop(_rimSprite);
                            return _rimSprite;
                        }

                    case RimSmoothing.Halo:
                        {
                            // Under each line a wider, faint one along the same outline, which
                            // fills in the half-covered pixels at its edges.
                            _rimHalo = compositor.CreateShapeVisual();
                            _rimHalo.Opacity = 0.3f;

                            foreach (var shape in _rimShapes)
                            {
                                var halo = compositor.CreateSpriteShape(shape.Geometry);
                                halo.StrokeBrush = shape.StrokeBrush;
                                halo.StrokeThickness = shape.StrokeThickness + 1;
                                _rimHalo.Shapes.Add(halo);
                            }

                            var both = compositor.CreateContainerVisual();
                            both.Children.InsertAtTop(_rimHalo);
                            both.Children.InsertAtTop(_rim);
                            Root.Children.InsertAtTop(both);
                            return both;
                        }

                    default:
                        Root.Children.InsertAtTop(_rim);
                        return _rim;
                }
            }

            // A blur of the given amount over a rim surface; one compiled effect per compositor,
            // the amount a property of each brush.
            private static CompositionEffectBrush CreateFeather(Compositor compositor, float amount)
            {
                if (!_featherFactories.TryGetValue(compositor, out var factory))
                {
                    factory = compositor.CreateEffectFactory(new GaussianBlurEffect
                    {
                        Name = "Blur",
                        BlurAmount = 1,
                        Optimization = EffectOptimization.Quality,
                        BorderMode = EffectBorderMode.Soft,
                        Source = new CompositionEffectSourceParameter("Rim")
                    }, new[] { "Blur.BlurAmount" });
                    _featherFactories.Add(compositor, factory);
                }

                var brush = factory.CreateBrush();
                brush.Properties.InsertScalar("Blur.BlurAmount", amount);
                return brush;
            }

            /// <summary>
            /// Lines of whole device pixels: the thickness rounded to them, at the scale the
            /// window is drawn at, and the inset moved so that the outer edge stays where it was.
            /// A one pixel line at 125% is otherwise 1.25 pixels, two of them half lit.
            /// </summary>
            private void SnapStrokes(Border host)
            {
                var scale = (float)(host.XamlRoot?.RasterizationScale ?? 1);

                for (int i = 0; i < _rimShapes.Length; i++)
                {
                    var drawn = _rimThicknesses[i];
                    var snapped = MathF.Max(1, MathF.Round(drawn * scale)) / scale;

                    _rimShapes[i].StrokeThickness = snapped;
                    _rimInsets[i] = _rimDrawnInsets[i] - drawn / 2 + snapped / 2;
                }
            }

            private ScalarKeyFrameAnimation CreateBrightness(Compositor compositor, RimAnimation animation)
            {
                (float, float)[] frames;
                double seconds;

                switch (animation)
                {
                    case RimAnimation.Pulse:
                        frames = new[] { (0f, 1f), (0.5f, 0.3f), (1f, 1f) };
                        seconds = 3.6;
                        break;
                    case RimAnimation.Heartbeat:
                        frames = new[] { (0f, 0.45f), (0.1f, 1f), (0.2f, 0.6f), (0.3f, 1f), (0.5f, 0.45f), (1f, 0.45f) };
                        seconds = 1.8;
                        break;
                    case RimAnimation.Flicker:
                        frames = new[] { (0f, 0.85f), (0.09f, 1f), (0.17f, 0.7f), (0.29f, 0.95f), (0.41f, 0.78f), (0.55f, 1f), (0.68f, 0.72f), (0.84f, 0.96f), (1f, 0.85f) };
                        seconds = 3.2;
                        break;
                    default:
                        return null;
                }

                var brightness = compositor.CreateScalarKeyFrameAnimation();

                foreach (var (progress, value) in frames)
                {
                    // Weaker: the dips are shallower, down to none at all.
                    brightness.InsertKeyFrame(progress, (float)(1 - (1 - value) * _motion), _ease);
                }

                brightness.Duration = TimeSpan.FromSeconds(seconds);
                brightness.IterationBehavior = AnimationIterationBehavior.Forever;
                return brightness;
            }

            /// <summary>
            /// Centres a gradient on the pointer, as the element sees it.
            /// </summary>
            private void FollowPointer(CompositionRadialGradientBrush brush)
            {
                _followers.Add(brush);
            }

            private readonly System.Collections.Generic.List<CompositionRadialGradientBrush> _followers = new();

            private readonly CompositionPropertySet _state;
            private readonly double _motion;
            private readonly CompositionEasingFunction _ease;
            private readonly RimHover _hover;
            private readonly CompositionLinearGradientBrush _rainbow;

            private UIElement _tracker;
            private PointerEventHandler _entered;
            private PointerEventHandler _exited;

            /// <summary>
            /// Starts following the pointer, for the effects that do: over the element the glass
            /// is behind - the host itself takes no input.
            /// </summary>
            public void Track(Border host)
            {
                if (_followers.Count == 0 && _hover is not (RimHover.Glow or RimHover.All))
                {
                    return;
                }

                _tracker = VisualTreeHelper.GetParent(host) as UIElement ?? host;

                var pointer = ElementCompositionPreview.GetPointerPositionPropertySet(_tracker);

                foreach (var brush in _followers)
                {
                    var center = _compositor.CreateExpressionAnimation("Vector2(p.Position.X - s.Origin.X, p.Position.Y - s.Origin.Y)");
                    center.SetReferenceParameter("p", pointer);
                    center.SetReferenceParameter("s", _state);
                    brush.StartAnimation("EllipseCenter", center);
                }

                _entered = (s, e) => AnimateHover(true);
                _exited = (s, e) => AnimateHover(false);

                // The wheel scrolls the element out from under a pointer that stays put, and no
                // exit comes for that: the glow stayed lit on a bubble long gone from the pointer.
                // A turn of the wheel counts as leaving; the next move over it, as coming back.
                _moved = (s, e) => AnimateHover(true);

                _tracker.AddHandler(UIElement.PointerEnteredEvent, _entered, true);
                _tracker.AddHandler(UIElement.PointerMovedEvent, _moved, true);
                _tracker.AddHandler(UIElement.PointerWheelChangedEvent, _exited, true);
                _tracker.AddHandler(UIElement.PointerExitedEvent, _exited, true);
                _tracker.AddHandler(UIElement.PointerCanceledEvent, _exited, true);
                _tracker.AddHandler(UIElement.PointerCaptureLostEvent, _exited, true);
            }

            public void Untrack()
            {
                if (_tracker != null)
                {
                    _tracker.RemoveHandler(UIElement.PointerEnteredEvent, _entered);
                    _tracker.RemoveHandler(UIElement.PointerMovedEvent, _moved);
                    _tracker.RemoveHandler(UIElement.PointerWheelChangedEvent, _exited);
                    _tracker.RemoveHandler(UIElement.PointerExitedEvent, _exited);
                    _tracker.RemoveHandler(UIElement.PointerCanceledEvent, _exited);
                    _tracker.RemoveHandler(UIElement.PointerCaptureLostEvent, _exited);
                    _tracker = null;
                }
            }

            private PointerEventHandler _moved;
            private bool _hovered;

            private void AnimateHover(bool over)
            {
                // Every move passes here: only a change starts an animation.
                if (_hovered == over)
                {
                    return;
                }

                _hovered = over;

                // From wherever it is now: no keyframe at 0.
                var hover = _compositor.CreateScalarKeyFrameAnimation();
                hover.InsertKeyFrame(1, over ? 1 : 0, _ease);
                hover.Duration = TimeSpan.FromSeconds(over ? 0.25 : 0.6);
                _state.StartAnimation("Hover", hover);
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

                _state.InsertVector2("Center", size / 2);
                _state.InsertScalar("Reach", size.Length() / 2);

                if (_rainbow != null)
                {
                    _rainbow.StartPoint = Vector2.Zero;
                    _rainbow.EndPoint = size;
                }

                // Where the host sits in the element the pointer is measured against.
                if (_tracker != null && _tracker != host)
                {
                    try
                    {
                        var origin = host.TransformToVisual(_tracker).TransformPoint(default);
                        _state.InsertVector2("Origin", new Vector2((float)origin.X, (float)origin.Y));
                    }
                    catch
                    {
                        // Not in the same tree for a moment, while it is being taken down.
                    }
                }

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

                if (_smoothing == RimSmoothing.PixelSnap)
                {
                    SnapStrokes(host);
                }
                else if (_smoothing is RimSmoothing.Blend or RimSmoothing.Soft)
                {
                    var padded = size + new Vector2(Feather * 2);
                    _rimSurface.SourceSize = padded;
                    _rimSprite.Size = padded;
                }
                else if (_rimHalo != null)
                {
                    _rimHalo.Size = size;
                }

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
                        run.InsertKeyFrame(0.65f, from + size, _ease);
                        run.InsertKeyFrame(1, from + size);
                        run.Duration = TimeSpan.FromSeconds(4.2);
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
