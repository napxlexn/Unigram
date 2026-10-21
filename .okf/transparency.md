---
type: Feature
title: Прозорість вікна (форк napxlexn)
description: "Матеріал вікна (Mica / Mica Alt / Acrylic / blur-behind) і два повзунки: непрозорість фону сторінок та непрозорість шпалер чату."
timestamp: "2026-10-21T00:00:00+00:00"
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

## Повідомлення, шапка, невидиме читання (2026-10-20, третя ітерація)
- `Common/MessageStyle.cs`: стилі бульбашок (`BubbleStyle`: Original/Glass/Frosted/Glow + `BubbleOpacity`), реакцій (`ReactionStyle`), сервісних капсул (`ServiceStyle`) і тексту (`MessageTextStyle`). Кольори проходять через `MessageStyle.Adjust` у `MessageBrushes.Update` і змінюються одразу. Розмиття (`SolidGaussianBrush` на кожен колір) і обідок (`ContentPanel.BorderBrush`) застосовуються, коли бульбашки реалізуються (`MessageStyle.Version`). Градієнтні бульбашки отримують альфу в `MessageBubbleBackgroundBrush.ApplyColorStops`.
- Обідок реакцій — накладений `Border` з `MessageReactionBorderBrush` у шаблоні `ReactionButton`, тож компонування він не змінює.
- Сервісні капсули: `MessageServiceBackgroundBrush` з `Accent.xaml` перефарбовується на місці (`SolidGaussianBrush.TintColor` перебудовує ефект).
- Скляна шапка: `FadingGlassBrush` (backdrop blur + тінт під маскою вертикального градієнта) на `DetailHeaderBackground` у `MasterDetailView`, висота +24, без нижньої лінії. Перемикається `IsGlassHeaderEnabled`.
- Невидиме читання: `AppSettings.IsGhostReadEnabled`. `ClientService.ViewMessages` пропускає пасивні `forceRead: false` в особистих чатах і групах, канали не зачіпає. Перевірено: `read_inbox` групи з 45 тис. непрочитаних не змінився під час перегляду.
## Шапка, власні кольори, нижня панель (2026-10-21, четверта ітерація)
- `HeaderStyle` (Original / Glass «Скло з переходом» / Capsule «Капсула») замінює `IsGlassHeaderEnabled`; старе значення мігрується. `MasterDetailView.ApplyHeaderStyle`: `CreateGlassHeaderBrush` (`FadingGlassBrush`, тінт ~0x14, blur 32) або `CreateCapsuleHeaderBrush` (заокруглена плашка з відступами) + `CreateRimBrush`. Мінімальний масштаб заголовка 0.75 (оригінал 0.583), центр масштабування — верх по центру.
- Власні кольори повідомлень: `UseCustomMessageColors` + `CustomIncoming/Outgoing/IncomingText/OutgoingText/Reaction` колір. Мають пріоритет над темою, варіантом і темою чату; градієнт вихідних замінюється. Вибір — muxc `ColorPicker` у Flyout. «Теми для чатів» і «Шпалери чату» продубльовано в блоці «Повідомлення».
- Майданчик стоїть над блоком «Повідомлення» і оновлюється на кожну зміну (`RefreshPlayground`).
- Нижня панель каналу (Mute + подарунок): окремий скляний шар `ChatFooterGlassBrush` (`SolidGaussianBrush`, тінт #8C161616 / #A6FFFFFF) з обідком `ChatFooterRimBrush`.
- Падіння: `FadingGlassBrush` пропускає маску при `SolidUntil >= 1` (дубль стопу → 0x80070057). Пензлі під час перемикання не диспозяться, а відпускаються (`FadingGlassBrush.OnDisconnected`, `PowerSavingBrushBase.Rebuild`), бо інакше RO_E_CLOSED 0x80000013. Перевірено 10 перемикань шапки та 6 капсул поспіль.

## Шапка чату, лінії, сяйво, гладке скло (2026-10-21, п'ята ітерація)
- `ChatHeaderStyle` (Original / Glass / FadingGlass / Capsule). У скляних стилях `RootGrid` переходить у рядок 0, `ClipperOuter` зсувається на висоту шапки, а `_headerInset` додається до `AnimatedHeight`: так повідомлення прокручуються під шапкою. Скло — окремий `HeaderGlass` поза `ClipperOuter`. Цей контейнер анімується й рендериться як окремий шар, тому backdrop-розмиття всередині нього не бачить повідомлень.
- `SeparatorMode` (Original / HideAll / HideDark / DarkToLight) замінює `HideSeparators` (мігрується). Чи лінія темна, визначається за її власним кольором (`Theme.GetSeparatorColor`). Режим охоплює `TelegramSeparatorMediumBrush` (лінія над коментарями поста) і WinUI-пензлі `CardStrokeColorDefaultBrush`, `SettingsItemBorderBrush` (перенесено з App.xaml в Theme) та `DividerStrokeColorDefaultBrush`.
- Бульбашки: Liquid (матове скло + обідок-відблиск), Glow («Лампове сяйво») і Terminal («Ретро-термінал»). `ContentGlow` для кожного джерела окремо копіює вміст у VisualSurface, розмиває й накладає з Screen-blend. Джерела — текст, лічильники реакцій, ім'я та рядок коментарів; медіа ніколи не світиться. Лінії розгортки Terminal лежать під вмістом (`ScanlineHost`). Реакції з Glow мають тепле світло зсередини (`MessageReactionGlowBrush`, muxc RadialGradientBrush).
- `SmoothGlass`: скло й обідок малюються композицією (згладжена маска з rounded rectangle shape + stroke). Причина: XAML обрізає композиційний фон заокругленого елемента без згладжування. Застосовано до футера каналу, капсули чату й капсули шапки сторінок.
- Шапка сторінок: властивості `TitleShift` / `ButtonShift` центрують заголовок і стрілку в стилях Glass і Capsule.
- Тестування: PrintWindow не малює backdrop-розмиття, тож скло перевіряти лише знімком екрана. Поверх тестового вікна можуть бути вікна користувача (Steam, Discord) — такі знімки видаляти.

## Спільне скло, обідки, відкриття без прочитання (2026-10-21, шоста ітерація)
- `GlassKind` — один перелік з 16 видів для всіх елементів: бічної панелі, шапки сторінок, шапки чату, нижньої панелі каналу, бульбашок, реакцій і сервісних капсул. Старі `PaneMaterial`/`BubbleStyle`/`ReactionStyle`/`ServiceStyle` мігруються в `PaneGlass`/`BubbleGlass`/`ReactionGlass`/`ServiceGlass`. Форма шапок (`HeaderStyle`, `ChatHeaderStyle`) задається окремо від виду скла.
- `Glass.Resolve(kind, dark)` повертає `GlassSpec` (тінт, розмиття, насиченість, перелив, внутрішнє світло, лінії розгортки, колір фосфору, власний обідок). Види без власного тінту беруть колір елемента (`HasOwnTint`).
- `SmoothGlass` малює будь-який вид контурами з окремим радіусом для кожного кута (Win2D path → CompositionPathGeometry) і так само згладжує бульбашки. `RimStyle` має 8 стилів обідка. Інтенсивність обідка, сяйва, ліній розгортки та розмиття задається повзунками. `ContentGlow` світить колір фосфору.
- Капсула шапки чату намальована на `HeaderGlass`, під панелями, тож шапка разом із закріпленим повідомленням і перекладом — один елемент.
- Фабрики ефектів кешуються для кожного компоновника (`ConditionalWeakTable`). Скляні бульбашки мають прозорий, а не null фон, щоб лишатися клікабельними.
- Відкрити без прочитання: пункт у контекстному меню списку чатів. `ClientService.OpenWithoutReading` веде лічильник по чату. `ViewMessages` для такого чату нічого не надсилає. `DialogViewModel` знімає позначку при закритті, а підзаголовок показує «без прочитання». Перевірено: `read_inbox` не змінюється.
