//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.Collections.Generic;
using Telegram.Common;
using Telegram.Controls.Media;

namespace Telegram.Services.Settings
{
    /// <summary>
    /// Every value the progressive glass - the composer's fading band and the fading header - is
    /// drawn from, kept so that the glass tuner can change any of them while the chat is on
    /// screen. Each is stored on its own, and one never set is its default.
    /// </summary>
    public static class GlassTuning
    {
        public enum ParameterKind
        {
            Slider,
            Toggle,
            Choice
        }

        /// <param name="Read">For a value that is an appearance setting of its own, how it is read;
        /// the others are kept here.</param>
        /// <param name="Choices">For a choice, what each whole value from zero stands for.</param>
        public sealed record Parameter(string Group, string Key, string Label, double Minimum, double Maximum, double Step, double Default, Func<double, string> Format, Func<double> Read = null, Action<double> Write = null, ParameterKind Kind = ParameterKind.Slider, string[] Choices = null)
        {
            public bool IsBound => Read != null;
        }

        public const string Input = "Input";
        public const string Header = "Header";

        private static readonly Dictionary<string, double> _cache = new();

        /// <summary>
        /// Raised with the key of the value that changed, on the thread that changed it.
        /// </summary>
        public static event EventHandler<string> Changed;

        private static string Percent(double value) => $"{value:0}%";
        private static string Pixels(double value) => $"{value:0} px";
        private static string Number(double value) => $"{value:0.00}";
        private static string Whole(double value) => $"{value:0}";
        private static string OnOff(double value) => value >= 0.5 ? "увімкнено" : "вимкнено";

        private static string Ease(double value) => (int)Math.Round(value) switch
        {
            0 => "м'яка (smoothstep)",
            1 => "рівна (лінійна)",
            2 => "найм'якша (smootherstep)",
            3 => "швидкий спад",
            _ => "повільний спад"
        };

        private static IEnumerable<Parameter> Shape(string target, string group)
        {
            yield return new(group, target + ".SolidOffset", "Зсув початку переходу (мінус - нижче)", -160, 80, 1, 0, Pixels);
            yield return new(group, target + ".Steps", "Кількість шарів розмиття", 1, 12, 1, 6, Whole);
            yield return new(group, target + ".Curve", "Крива сили шарів (степінь)", 0.25, 4, 0.05, 2, Number);
            yield return new(group, target + ".Overlap", "Перекриття шарів (у кроках)", 0.5, 6, 0.1, 2, Number);
            yield return new(group, target + ".Ease", "Форма згасання", 0, 4, 1, 0, Ease, Kind: ParameterKind.Choice, Choices: new[] { Ease(0), Ease(1), Ease(2), Ease(3), Ease(4) });
            yield return new(group, target + ".Saturation", "Насиченість під склом", 0, 3, 0.05, 1.2, Number);
            yield return new(group, target + ".Quality", "Якісне розмиття", 0, 1, 1, 1, OnOff, Kind: ParameterKind.Toggle);
            yield return new(group, target + ".Tint", "Сила тонування", 0, 200, 5, 100, Percent);
            yield return new(group, target + ".TintSpread", "Довжина згасання тонування", 0, 200, 5, 100, Percent);
            yield return new(group, target + ".TintOffset", "Зсув тонування (мінус - нижче)", -160, 80, 1, 0, Pixels);
            yield return new(group, target + ".Outline", "Показати межі: жовта - початок переходу, блакитна - тонування, червона - край", 0, 1, 1, 0, OnOff, Kind: ParameterKind.Toggle);
        }

        public static readonly IReadOnlyList<Parameter> All = Build();

        private static List<Parameter> Build()
        {
            var appearance = AppSettings.Appearance;
            var input = "Скло над полем вводу й кнопкою каналу (стиль «Зникаюче»)";
            var header = "Скло шапки чату (стиль «Зникаюче скло»)";

            var glass = Glass.Options;
            var glassNames = glass.ConvertAll(x => x.Text).ToArray();

            // The styles and kinds of glass, as the appearance page has them: the chat's whole
            // glass is laid out again for these.
            Parameter Kind(string group, string key, string label, string[] choices, Func<int> read, Action<int> write)
            {
                return new(group, key, label, 0, choices.Length - 1, 1, read(), x => choices[Math.Clamp((int)x, 0, choices.Length - 1)], () => read(), x =>
                {
                    write((int)x);
                    AppearanceSettings.RaiseGlassHeaderChanged();
                }, ParameterKind.Choice, choices);
            }

            int IndexOf(GlassKind kind) => Math.Max(0, glass.FindIndex(x => x.Value == kind));

            var all = new List<Parameter>
            {
                Kind(input, "InputStyle", "Стиль поля вводу", new[] { "Оригінал", "Скляна капсула", "Скляна смуга на всю ширину", "Скло з переходом (зникаюче)" }, () => (int)appearance.InputStyle, x => appearance.InputStyle = (InputStyle)x),
                Kind(input, "InputGlass", "Вид скла поля вводу", glassNames, () => IndexOf(appearance.InputGlass), x => appearance.InputGlass = glass[x].Value),
                new(input, "InputBlur", "Сила розмиття", 0, 200, 5, 100, Percent, () => appearance.InputBlur, x => appearance.InputBlur = (int)x),
                new(input, "InputFadeHeight", "Висота переходу над полем", 0, 160, 1, 24, Pixels, () => appearance.InputFadeHeight, x => appearance.InputFadeHeight = (int)x),
                new(input, "InputFill", "Помітність поля вводу", 0, 100, 5, 45, Percent, () => appearance.InputFill, x => appearance.InputFill = (int)x),
            };
            all.AddRange(Shape(Input, input));

            all.Add(Kind(header, "ChatHeaderStyle", "Стиль шапки чату", new[] { "Оригінал", "Скло на всю ширину", "Зникаюче скло", "Капсула" }, () => (int)appearance.ChatHeaderStyle, x => appearance.ChatHeaderStyle = (ChatHeaderStyle)x));
            all.Add(Kind(header, "ChatHeaderGlass", "Вид скла шапки", glassNames, () => IndexOf(appearance.ChatHeaderGlass), x => appearance.ChatHeaderGlass = glass[x].Value));
            all.Add(new(header, Header + ".Blur", "Сила розмиття", 0, 200, 5, 100, Percent));
            all.Add(new(header, Header + ".Fade", "Висота переходу під шапкою", 0, 160, 1, 32, Pixels));
            all.AddRange(Shape(Header, header));

            return all;
        }

        public static Parameter Find(string key)
        {
            foreach (var parameter in All)
            {
                if (parameter.Key == key)
                {
                    return parameter;
                }
            }

            return null;
        }

        public static double Get(string key)
        {
            var parameter = Find(key);
            if (parameter == null)
            {
                return 0;
            }

            if (parameter.IsBound)
            {
                return parameter.Read();
            }

            if (!_cache.TryGetValue(key, out var value))
            {
                value = Math.Clamp(AppSettings.Appearance.GetValueOrDefault("Tune." + key, parameter.Default), parameter.Minimum, parameter.Maximum);
                _cache[key] = value;
            }

            return value;
        }

        public static void Set(string key, double value)
        {
            var parameter = Find(key);
            if (parameter == null)
            {
                return;
            }

            value = Math.Clamp(Math.Round(value / parameter.Step) * parameter.Step, parameter.Minimum, parameter.Maximum);

            if (Get(key) == value)
            {
                return;
            }

            if (parameter.IsBound)
            {
                parameter.Write(value);
            }
            else
            {
                _cache[key] = value;
                AppSettings.Appearance.AddOrUpdateValue("Tune." + key, value);
            }

            Changed?.Invoke(null, key);
        }

        public static void Reset(string key)
        {
            if (Find(key) is Parameter parameter)
            {
                Set(key, parameter.Default);
            }
        }

        /// <summary>
        /// The shape of a progressive glass as the values for <paramref name="target"/> have it.
        /// </summary>
        public static ProgressiveGlass.Shape GetShape(string target)
        {
            return new ProgressiveGlass.Shape(
                Steps: (int)Get(target + ".Steps"),
                Curve: (float)Get(target + ".Curve"),
                Overlap: (float)Get(target + ".Overlap"),
                Ease: (int)Get(target + ".Ease"),
                Saturation: (float)Get(target + ".Saturation"),
                Quality: Get(target + ".Quality") >= 0.5,
                Tint: (float)(Get(target + ".Tint") / 100),
                TintSpread: (float)(Get(target + ".TintSpread") / 100),
                Outline: Get(target + ".Outline") >= 0.5);
        }
    }
}
