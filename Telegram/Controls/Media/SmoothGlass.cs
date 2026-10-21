//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System.Numerics;
using Telegram.Common;
using Telegram.Navigation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

namespace Telegram.Controls.Media
{
    /// <summary>
    /// A rounded pane of glass with light along its rim, with smooth corners.
    ///
    /// A composition brush set as the background of a rounded XAML element is cut to the corners
    /// without antialiasing, and a XAML border drawn over it is antialiased - so the glass showed
    /// in steps outside the rim at every corner. Here both are Composition: the glass is masked by
    /// a rounded rectangle drawn as a shape, which is antialiased, rendered once into a surface the
    /// way <see cref="SolidGaussianBrush.CreateCircleBrush"/> does its circle; the rim is the same
    /// shape stroked, half a pixel in so that the line lands on whole pixels.
    ///
    /// The host is an empty Border behind the content; its CornerRadius is the glass's.
    /// </summary>
    public static class SmoothGlass
    {
        public static void Attach(Border host, Color tint, Color rimTop, Color rimBottom)
        {
            Detach(host);

            var compositor = BootStrapper.Current.Compositor;
            var attachment = new Attachment(compositor, tint, rimTop, rimBottom);

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
        /// The glass of the channel footer and the capsules: a strong blur under a tint that is
        /// dark on dark and light on light, and light catching the rim from above.
        /// </summary>
        public static void AttachDefault(Border host, ElementTheme theme)
        {
            if (theme == ElementTheme.Light)
            {
                Attach(host, Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            }
            else
            {
                Attach(host, Color.FromArgb(0x8C, 0x16, 0x16, 0x16), Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            }
        }

        private sealed class Attachment
        {
            public readonly ContainerVisual Root;
            public long Token;

            private readonly CompositionRoundedRectangleGeometry _maskGeometry;
            private readonly ShapeVisual _maskShape;
            private readonly CompositionVisualSurface _maskSurface;
            private readonly SpriteVisual _glass;

            private readonly CompositionRoundedRectangleGeometry _rimGeometry;
            private readonly ShapeVisual _rim;
            private readonly CompositionLinearGradientBrush _rimBrush;

            public Attachment(Compositor compositor, Color tint, Color rimTop, Color rimBottom)
            {
                Root = compositor.CreateContainerVisual();

                _maskGeometry = compositor.CreateRoundedRectangleGeometry();

                var maskFill = compositor.CreateSpriteShape(_maskGeometry);
                maskFill.FillBrush = compositor.CreateColorBrush(Colors.White);

                _maskShape = compositor.CreateShapeVisual();
                _maskShape.Shapes.Add(maskFill);

                _maskSurface = compositor.CreateVisualSurface();
                _maskSurface.SourceVisual = _maskShape;

                var fallback = tint;
                fallback.A = 0xF0;

                var masked = compositor.CreateMaskBrush();
                masked.Mask = compositor.CreateSurfaceBrush(_maskSurface);
                masked.Source = PowerSavingPolicy.AreMaterialsEnabled
                    ? SolidGaussianBrush.CreateBrush(compositor, tint)
                    : compositor.CreateColorBrush(fallback);

                _glass = compositor.CreateSpriteVisual();
                _glass.Brush = masked;
                Root.Children.InsertAtTop(_glass);

                _rimBrush = compositor.CreateLinearGradientBrush();
                _rimBrush.MappingMode = CompositionMappingMode.Absolute;
                _rimBrush.ColorStops.Add(compositor.CreateColorGradientStop(0, rimTop));
                _rimBrush.ColorStops.Add(compositor.CreateColorGradientStop(1, rimBottom));

                _rimGeometry = compositor.CreateRoundedRectangleGeometry();
                _rimGeometry.Offset = new Vector2(0.5f);

                var rimStroke = compositor.CreateSpriteShape(_rimGeometry);
                rimStroke.StrokeBrush = _rimBrush;
                rimStroke.StrokeThickness = 1;

                _rim = compositor.CreateShapeVisual();
                _rim.Shapes.Add(rimStroke);
                Root.Children.InsertAtTop(_rim);
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
                var radius = (float)host.CornerRadius.TopLeft;

                // Never more than half the height: a pill, not a broken shape.
                radius = System.Math.Min(radius, System.Math.Min(size.X, size.Y) / 2);

                _maskGeometry.Size = size;
                _maskGeometry.CornerRadius = new Vector2(radius);
                _maskShape.Size = size;
                _maskSurface.SourceSize = size;
                _glass.Size = size;

                // Before the first layout the size is zero, and the rim's would be negative.
                _rimGeometry.Size = Vector2.Max(size - Vector2.One, Vector2.Zero);
                _rimGeometry.CornerRadius = new Vector2(System.Math.Max(0, radius - 0.5f));
                _rim.Size = size;

                _rimBrush.StartPoint = Vector2.Zero;
                _rimBrush.EndPoint = new Vector2(0, System.Math.Max(1, size.Y));
            }
        }
    }
}
