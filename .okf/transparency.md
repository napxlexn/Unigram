---
type: Feature
title: Прозорість вікна (форк napxlexn)
description: "Матеріал вікна (Mica / Mica Alt / Acrylic / blur-behind) і два повзунки: непрозорість фону сторінок та непрозорість шпалер чату."
timestamp: "2026-10-20T00:00:00+00:00"
---

# Прозорість вікна

Власна функція форку. Підхід перенесено з AI Limits (blur-behind + повзунок непрозорості фону)
і з вікна налаштувань SmartWidget (Mica + напівпрозорі шари).

## Налаштування
`Telegram/Services/Settings/AppearanceSettings.cs`:
- `WindowMaterial` (enum `None, Mica, MicaAlt, Acrylic, BlurBehind`), за замовчуванням `Mica`, тобто стокова поведінка.
- `BackgroundOpacity`: 10–100 %, за замовчуванням 100.
- `WallpaperOpacity`: 0–100 %, за замовчуванням 100.
- Статична подія `TransparencyChanged`, щоб зміни застосовувалися наживо.

UI: блок «Прозорість» у `Views/Settings/SettingsAppearancePage.xaml`, властивості у `SettingsAppearanceViewModel` (регіон `Transparency`).

## Як це працює
- **Матеріал вікна, Win32-хост** (`Telegram.Win32.csproj`): `Host/WindowBackdrop.cs`.
  - Mica, Mica Alt і Acrylic задаються через `DWMWA_SYSTEMBACKDROP_TYPE` (Mica Alt та Acrylic лише з 22621).
  - Blur-behind задається через `SetWindowCompositionAttribute` + `ACCENT_ENABLE_BLURBEHIND (3)` з тінтом ~11 % (`Host/Interop.cs`).
  - Blur-behind перезапускається (вимкнути → увімкнути) на `WM_EXITSIZEMOVE` і `WM_DPICHANGED` (`Host/IslandWindow.cs`).
  - Якщо в Windows вимкнено прозорість або увімкнено high contrast, вікно отримує суцільний фон.
- **Матеріал вікна, UWP-хост:** лише `BackdropMaterial` (Mica або нічого). Налаштування читається один раз під час створення вікна. `WindowContext.SupportsWindowMaterials` визначає, які варіанти показувати.
- **Непрозорість фону:** `Common/Theme.cs`, множина `_surfaces`. Альфа фонових ключів теми (`ApplicationPageBackgroundThemeBrush`, `PageHeaderBackgroundBrush` та інші) множиться на коефіцієнт. Bot API `ThemeParameters` отримує непрозорі кольори. Зміна запускає `NightModeService.Update(false)` з дебаунсом 150 мс.
- **Непрозорість шпалер:** `Controls/Chats/ChatBackgroundControl.cs`, `Opacity` всього контрола, тож разом зі шпалерами зникає і чорний шар затемнення.

## Не перевірено
Збірку та роботу в середовищі ще не перевіряли: на машині не було Visual Studio 2026.
