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
- `WindowMaterial` (`None, Mica, MicaAlt, Acrylic, BlurBehind`), за замовчуванням `Mica`.
- `BackgroundOpacity`: 10–100 %, за замовчуванням 70. Це щільність тінту поверх матеріалу.
- `IsWallpaperEnabled` (за замовчуванням true) і `WallpaperOpacity` 0–100 %.
- Події `MaterialChanged`, `BackgroundOpacityChanged` і `WallpaperOpacityChanged` розділені навмисно, щоб повзунки не перезапускали розмиття.

UI: блок «Прозорість» у `Views/Settings/SettingsAppearancePage.xaml` (матеріал, повзунок фону, «Показувати шпалери в чатах», повзунок шпалер).

## Як це працює (Win32-хост)
- **Модель AI Limits.** Матеріал (DWM) лежить знизу. Над ним один шар тінту на `WindowPresenter` (`Host/WindowBackdrop.cs`, `_background`) з альфою `BackgroundOpacity`. Усі поверхні сторінок прозорі (`Theme.GetSurfaceOpacity()` == 0), тому щільність скла однакова по всьому вікну.
- **Шари з фреймворк-пензлями** (`MaterialPart` у `MasterDetailView`, панель папок у `MainPage`) позначені `common:Theme.IsSurface="True"`. Поки матеріал увімкнений, їхній фон прибирається.
- **Смуги заголовка й поля введення** (`PageSubHeaderBackgroundBrush2`/`...SolidBrush` з `App.xaml`) мають альфу `BackgroundOpacity`, щоб повідомлення під ними не просвічували крізь текст. Пензлі змінюються на місці в `Theme.UpdateApplicationSurfaces`.
- **Blur-behind:** `SetWindowCompositionAttribute`, `ACCENT_ENABLE_BLURBEHIND`, `AccentFlags = 0`. З `flags = 2` Windows 11 малює тінт непрозорим, і розмиття не видно. Розмиття перезапускається після `WM_EXITSIZEMOVE` і `WM_DPICHANGED`.
- **Шпалери:** `ChatBackgroundControl.UpdateOpacity`. При 0 % або вимкненому прапорці `Presenter` згортається (Collapsed): з `Opacity = 0` composition-візуали шпалер лишалися видимими.

## UWP-хост
Лише `BackdropMaterial` (Mica або нічого), а альфа множиться на поверхнях.

## E2E-тестування
Режим `--no-activate` (`Host/IslandWindow.cs`) з драйвером `Host/E2eDriver.cs`: команди через `LocalState\e2e\cmd.txt` — `nav`, `chat`, `slider`, `combo`, `check`, `show`, `probe x y`, `state`, `night`, `accent/sbt/extend`. UI Automation не використовувати: вона дає застосунку право забрати передній план. Тестовий exe перевести на GUI-підсистему (`editbin /SUBSYSTEM:WINDOWS`), бо консольний відкриває вікно терміналу.
## Стан
2026-10-20: перевірено e2e на другому моніторі (Win32 Debug x64): усі 5 матеріалів, повзунки, вимикач шпалер, світла тема, збереження після перезапуску. Telegram.Modern (UWP) компілюється. Як зібрати: [[local-build]].

## Додатково (2026-10-20, друга ітерація)
- Win32: Mica і Mica Alt прибрано (`WindowMaterial` повертає `BlurBehind` замість них). Варіанти: «Немає», «Акрил», «Розмиття». За замовчуванням — «Розмиття».
- `PaneMaterial` (Оригінал / Матове скло / Легке скло / Суцільний) для `SplitView` у `Views/Host/RootWindow.xaml.cs`. Скло — `SolidGaussianBrush`, тобто Composition backdrop blur, бо XAML `AcrylicBrush` в Islands не працює.
- «Розташування папок» у блоці «Прозорість» дублює налаштування з «Папки для чатів» (`AppSettings.UseLeftTabsForChats` + `UpdateChatFoldersLayout`).
- Експериментальний `HideSeparators` обнуляє `NavigationViewContentGridBorderBrush` (пишеться в `Theme.Update` зі значенням WinUI), `NavigationViewItemSeparatorForeground` і `PageHeaderBorderBrush`.
- Нижня панель каналу (`ChatBottomButton`) використовує `ChatFooterBackgroundBrush` з альфою ≥ 0.9.
- Тема з оригіналу (Night, акцент `#917BBD`, градієнт `258DE5~4272DF~8146D7~9F3EAA`, шпалери `LqK3YYoX2EnrCgAAJ63F4zvk4wg`) імпортується командою драйвера `import-original` як варіант Night.

## Ресурси
- Без жорсткого витоку: Event і Semaphore звільняються лише під час повного GC. `Host/HandleCollector.cs` запускає фоновий GC, коли кількість хендлів виростає на 300. У простої вона тримається в межах 1,9–2,1 тис.
- Лог TDLib: за замовчуванням рівень 2, ліміт 10 МБ. Кеш `.tgfc` обмежено 256 МБ.
- Не зроблено (ризиковано): один спільний екземпляр LibVLC і `--avcodec-threads`, автоочищення `temp` через `optimizeStorage`.