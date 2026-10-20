//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System.Numerics;
using Telegram.Common;
using Telegram.Navigation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Telegram.Controls.Media
{
    /// <summary>
    /// Glass that fades out downwards: a blur of whatever scrolls beneath, under a tint, both
    /// masked by a vertical gradient. Content therefore slides under it gradually instead of
    /// meeting a hard, darker bar - which is what the settings header used to be.
    /// </summary>
    public partial class FadingGlassBrush : XamlCompositionBrushBase
    {
        private CompositionBrush _brush;

        protected override void OnConnected()
        {
            var compositor = BootStrapper.Current.Compositor;

            // Opaque at the top, holding through the title, gone at the bottom edge.
            var mask = compositor.CreateLinearGradientBrush();
            mask.StartPoint = new Vector2(0, 0);
            mask.EndPoint = new Vector2(0, 1);
            mask.ColorStops.Add(compositor.CreateColorGradientStop(0, Color.FromArgb(255, 255, 255, 255)));
            mask.ColorStops.Add(compositor.CreateColorGradientStop((float)SolidUntil, Color.FromArgb(255, 255, 255, 255)));
            mask.ColorStops.Add(compositor.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255)));

            CompositionBrush source;
            if (PowerSavingPolicy.AreMaterialsEnabled)
            {
                var blur = new GaussianBlurEffect
                {
                    Name = "Blur",
                    BlurAmount = (float)BlurAmount,
                    Optimization = EffectOptimization.Speed,
                    BorderMode = EffectBorderMode.Hard,
                    Source = new CompositionEffectSourceParameter("Backdrop")
                };

                var composite = new CompositeEffect { Mode = CanvasComposite.SourceOver };
                composite.Sources.Add(blur);
                composite.Sources.Add(new ColorSourceEffect { Color = TintColor });

                var effect = compositor.CreateEffectFactory(composite).CreateBrush();
                effect.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
                source = effect;
            }
            else
            {
                source = compositor.CreateColorBrush(TintColor);
            }

            var masked = compositor.CreateMaskBrush();
            masked.Source = source;
            masked.Mask = mask;

            _brush = masked;
            CompositionBrush = masked;
        }

        protected override void OnDisconnected()
        {
            _brush?.Dispose();
            _brush = null;
            CompositionBrush = null;
        }

        public Color TintColor { get; set; }

        public double BlurAmount { get; set; } = 20;

        /// <summary>
        /// Where the fade starts, as a fraction of the height.
        /// </summary>
        public double SolidUntil { get; set; } = 0.55;
    }
}
