//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using Telegram.Navigation;
using Telegram.Services;
using Telegram.Services.Settings;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Telegram.Host
{
    /// <summary>
    /// Mica behind a window, with WinUI 2's <c>BackdropMaterial</c> fallbacks - which is the part
    /// worth copying. The effect itself is one DWM call; what makes it behave is knowing when not
    /// to use it, and what to put there instead.
    ///
    /// The rules, read out of BackdropMaterial.cpp and MicaController.cpp:
    ///
    /// - Not supported at all -> a solid colour, from the theme.
    /// - High contrast -> the system's own background colour, never the Mica tint.
    /// - Transparency effects off in Settings -> the same solid fallback.
    /// - Supported and on -> the target's Background goes fully **transparent**, deliberately, so
    ///   that hit testing behaves the same with the material as without it.
    ///
    /// The fallback colours are Mica's own tints, so the window looks the same shape either way:
    /// #F3F3F3 light, #202020 dark - MicaController::sc_lightThemeColor and sc_darkThemeColor.
    ///
    /// One of these per window. It holds the UISettings and AccessibilitySettings instances rather
    /// than fetching them per call: their events stop firing if the object is collected.
    /// </summary>
    internal sealed class WindowBackdrop
    {
        private static readonly Color LightTint = Color.FromArgb(255, 243, 243, 243);
        private static readonly Color DarkTint = Color.FromArgb(255, 32, 32, 32);

        private readonly IntPtr _hwnd;
        private readonly WindowPresenter _presenter;

        private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();

        private readonly UISettings _settings = new();
        private readonly AccessibilitySettings _accessibility = new();

        // The blur-behind tint, AABBGGRR: a near-black at ~11%, the same as AI Limits. The tint
        // layer on the presenter carries the real colour; this only keeps bright windows behind
        // from glaring.
        private const uint BlurTintDark = 0x1C121212;
        private const uint BlurTintLight = 0x1CF3F3F3;

        // Every live backdrop by its HWND, so that the window procedure can ask for the blur to
        // be restarted after a drag or a DPI change without holding on to the owner.
        private static readonly Dictionary<IntPtr, WindowBackdrop> _windows = new();

        private bool _active;
        private WindowMaterial _material = WindowMaterial.None;
        private bool _blurDark;

        // One brush for the life of the window, recoloured rather than replaced: the opacity slider
        // moves it on every step.
        private readonly SolidColorBrush _background = new();

        public WindowBackdrop(IntPtr hwnd, WindowPresenter presenter)
        {
            _hwnd = hwnd;
            _presenter = presenter;

            _settings.AdvancedEffectsEnabledChanged += OnAdvancedEffectsEnabledChanged;
            _accessibility.HighContrastChanged += OnHighContrastChanged;
            _presenter.ActualThemeChanged += OnActualThemeChanged;
            AppearanceSettings.MaterialChanged += OnMaterialChanged;
            AppearanceSettings.BackgroundOpacityChanged += OnMaterialChanged;

            _windows[hwnd] = this;

            Update();
        }

        public void Release()
        {
            _settings.AdvancedEffectsEnabledChanged -= OnAdvancedEffectsEnabledChanged;
            _accessibility.HighContrastChanged -= OnHighContrastChanged;
            _presenter.ActualThemeChanged -= OnActualThemeChanged;
            AppearanceSettings.MaterialChanged -= OnMaterialChanged;
            AppearanceSettings.BackgroundOpacityChanged -= OnMaterialChanged;

            _windows.Remove(_hwnd);

            if (_active)
            {
                Apply(WindowMaterial.None);
                _active = false;
            }
        }

        /// <summary>
        /// DWM sometimes drops the blur-behind accent when the window is dragged across monitors
        /// or changes scale, leaving a flat tint. Turning it off and on again brings it back.
        /// </summary>
        public static void Refresh(IntPtr hwnd)
        {
            if (_windows.TryGetValue(hwnd, out var backdrop) && backdrop._active && backdrop._material == WindowMaterial.BlurBehind)
            {
                backdrop.Apply(WindowMaterial.BlurBehind);
            }
        }

        private void OnMaterialChanged(object sender, EventArgs args)
        {
            Post();
        }

        // These arrive on a system thread, so they are marshalled before touching XAML.
        private void OnAdvancedEffectsEnabledChanged(UISettings sender, object args)
        {
            Post();
        }

        private void OnHighContrastChanged(AccessibilitySettings sender, object args)
        {
            Post();
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            Update();
        }

        private void Post()
        {
            if (_queue == null || !_queue.TryEnqueue(Update))
            {
                Update();
            }
        }

        private void Update()
        {
            // Before the backdrop, and whether or not it is on: this is what tells DWM which tint
            // to draw Mica in, and what colours the frame it still draws on three sides. Left
            // alone it follows the system theme, so an app forced to dark against a light system
            // gets a light Mica behind a dark window.
            Win32.SetDarkMode(_hwnd, _presenter.ActualTheme == ElementTheme.Dark);

            var material = AppSettings.Appearance.WindowMaterial;
            if (!IsSupported(material) || _accessibility.HighContrast || !_settings.AdvancedEffectsEnabled)
            {
                material = WindowMaterial.None;
            }

            // Blur-behind is also re-applied when the theme flips, since its tint follows it.
            var dark = _presenter.ActualTheme == ElementTheme.Dark;
            if (material != _material || (material == WindowMaterial.BlurBehind && dark != _blurDark))
            {
                // The XAML backstop only has to come off once per thread, and it never goes back
                // on: turning it off is what lets the backdrop reach the client area at all.
                if (material != WindowMaterial.None)
                {
                    WindowPrivate.TrySetTransparentBackground(true);
                }

                // Off first, whatever comes next: a system backdrop and the accent policy draw on
                // top of each other if both are left set.
                if (_material != WindowMaterial.None && _material != material)
                {
                    Apply(WindowMaterial.None);
                }

                _active = Apply(material) && material != WindowMaterial.None;
                _material = _active ? material : WindowMaterial.None;
                _blurDark = dark;
            }

            // With the material on, the pages above are transparent (Theme.GetSurfaceOpacity) and this
            // is the one layer that decides how dense the glass is - the AI Limits model: a clear
            // backdrop, then the theme colour at the user's opacity.
            var color = Fallback();
            if (_active)
            {
                color.A = (byte)(255 * AppSettings.Appearance.BackgroundOpacity / 100);
            }

            _background.Color = color;

            if (_presenter.Background != _background)
            {
                _presenter.Background = _background;
            }
        }

        private static bool IsSupported(WindowMaterial material) => material switch
        {
            WindowMaterial.None => false,
            WindowMaterial.Mica => Win32.IsBackdropSupported,
            WindowMaterial.MicaAlt => Win32.IsBackdropTypeSupported(Win32.DWMSBT_TABBEDWINDOW),
            WindowMaterial.Acrylic => Win32.IsBackdropTypeSupported(Win32.DWMSBT_TRANSIENTWINDOW),
            // The accent policy is older than Mica and works on Windows 10 as well.
            WindowMaterial.BlurBehind => true,
            _ => false
        };

        private bool Apply(WindowMaterial material)
        {
            switch (material)
            {
                case WindowMaterial.Mica:
                    return Win32.SetBackdrop(_hwnd, Win32.DWMSBT_MAINWINDOW);
                case WindowMaterial.MicaAlt:
                    return Win32.SetBackdrop(_hwnd, Win32.DWMSBT_TABBEDWINDOW);
                case WindowMaterial.Acrylic:
                    return Win32.SetBackdrop(_hwnd, Win32.DWMSBT_TRANSIENTWINDOW);
                case WindowMaterial.BlurBehind:
                    var tint = _presenter.ActualTheme == ElementTheme.Dark ? BlurTintDark : BlurTintLight;
                    return Win32.SetBlurBehind(_hwnd, true, tint);
                default:
                    Win32.SetBlurBehind(_hwnd, false, 0);
                    Win32.SetBackdrop(_hwnd, Win32.DWMSBT_NONE);
                    return true;
            }
        }

        /// <summary>
        /// High contrast wins over the theme: the system's background colour is the only one that
        /// respects the user's scheme. Otherwise Mica's own tint, so that turning the effect off
        /// does not change the window's colour, only its depth.
        /// </summary>
        private Color Fallback()
        {
            if (_accessibility.HighContrast)
            {
                return _settings.GetColorValue(UIColorType.Background);
            }

            return _presenter.ActualTheme == ElementTheme.Dark ? DarkTint : LightTint;
        }
    }
}
