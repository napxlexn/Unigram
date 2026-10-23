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
- Власного майданчика більше немає: блок «Повідомлення» стоїть одразу під стандартним попереднім переглядом теми (`Message1`/`Message2` плюс капсула дати), який оновлюється на кожну зміну (`RefreshPreview`).
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

## Обідок (налаштування)

- Окремий блок «Обідок»: стиль, колір (`RimColorMode`: як у скла / акцент / колір елемента / власний `RimCustomColor`), світло (`RimLight`: як у стилю / рівне / згори / знизу / з кута), анімація (`RimAnimation`: дихання — `Opacity` обідка; відблиск — смуга градієнта, у якої анімуються `StartPoint`/`EndPoint`, бо анімація `Offset` не рухалась), товщина `RimWidth` 50–400 %, яскравість.
- Де показувати: `RimOnBubbles`, `RimOnReactions`, `RimOnPanels` (передаються як `rim:` у `SmoothGlass.Attach`; реакції — у `MessageStyle.GetReactionRim`).
- `Glass.GetRimColor(element, dark)` — спільний вибір кольору для `SmoothGlass` і реакцій.
- `GlassHost` бульбашки отримує від'ємний `Margin` = `ContentPanel.Padding`: інакше скло й обідок починалися на 4 px нижче верху бульбашки (текст тиснув у обідок), а градієнт теми на вихідних було видно смугою над обідком — це і є «ореол».
- Анімації обідка (`RimAnimation`): дихання, серцебиття, свічка (кадри яскравості `_rim.Opacity` з easing), відблиск (м'яка смуга, анімуються `StartPoint`/`EndPoint`), світло по колу (вираз Cos/Sin від `Angle` у `_state`), райдужний перелив (`ColorKeyFrameAnimation` у HSL на кожній зупинці).
- Реакція на курсор (`RimHover`: світло за курсором, сяйво при наведенні, відблиск на склі, усе разом): `ElementCompositionPreview.GetPointerPositionPropertySet` батька хоста (сам хост не приймає введення), `Origin` хоста в ньому оновлюється в `Update`, `Hover` анімується на PointerEntered/Exited (обробники знімаються в `Detach`).

## Сторінка «Вигляд»

- Попередній перегляд винесено із ScrollViewer у закріплену `PreviewPanel` (рядок 0): налаштування прокручуються під ним. Кнопка «Матовість» (`PreviewHaze`) ховає напівпрозору картку під переглядом.
- Повзунки «Сила анімації» (`RimAnimationIntensity`: глибина провалів яскравості, яскравість відблисків) і «Сила реакції на курсор» (`RimHoverIntensity` → `_state.Power` у виразах).
- Згладжування обідка (`RimSmoothing`, експериментальне): стандартне; під пікселі (товщина в цілих пікселях пристрою з `XamlRoot.RasterizationScale`, зовнішній край на місці); чітке з м'яким краєм (живий `VisualSurface` з `_rim` + розмиття 1.1 під ним); м'яке розмиття (`_rim` у поверхні, розмиття 0.7); подвійний контур (ширша бліда копія кожної лінії). Суперсемплінг через `VisualSurface` зі `Scale = 2` не згладжував (поверхня рендериться в 1×), тому його прибрано.
- Окремий обідок для повідомлень: `RimProfile` (усі налаштування обідка разом); `SharedRim` — спільний, `BubbleRim` — з префіксом `Bubble`, де незмінене значення падає на спільне. `RimFor(bubble)`; `SmoothGlass.Attach(..., bubble: true)` для бульбашок, реакції беруть профіль бульбашок. У налаштуваннях: «Окремий обідок для повідомлень» і «Налаштовую обідок для» — усі контролі блоку редагують вибраний профіль. Яскравість обідка до 200 %.
- Ореол під скляними бульбашками: `ShadowCaster` (лінія висоти `MessageElevationBrush` 2 px) просвічував крізь скло — під склом прихований (`UpdateShadowCaster`). Відступ між згрупованими скляними бульбашками 5 px замість 2.
- Сяйво реакцій: `ContentGlow` на `GlowHost` шаблону `ReactionButton` (емодзі + цифра) для видів з люмінофором; для монохромних (термінали, свічка) цифра й обрана капсула — колір люмінофора; неон — обрана капсула напівпрозора. Окремі повзунки: `BubbleGlowIntensity`, `ReactionGlowIntensity`, `RimGlowIntensity`; `GlowIntensity` тепер «Внутрішнє світло».
- Поле вводу повідомлення (`InputStyle`: оригінал / скляна капсула `TextGlass` у `Footer` / смуга `InputGlassBand` на всю ширину / скло з переходом — `FadingGlassBrush.FadesUp`), скло `InputGlass`. Під склом `ShadowCaster` поля й `LinearCaster` вимкнені. Інші поля вводу (`FieldGlass`): `Theme` пише `TextControlBackground*` — WinUI-значення або тон скла.
- Шапка-капсула: висота шапки +8 (інші скляні +4), вміст по центру без зсуву; `MessagesRoot` обрізається геометричним кліпом по контуру капсули (верх і заокруглення, смуги поруч до її низу), далі на всю ширину.
- Заокруглення: `HeaderRadius` (капсули шапок), `CardRadius` (`HeaderedControl.ApplyCardLayout`), `ControlRadius` → `ControlCornerRadius`, який пише `Theme` (не конструктор `App`: у Win32 він не в UI-потоці й падає з 0x8001010E). `CheckBox` має власні 4 px.
- Заокруглення — дерево `RadiusTree` (загальне → 5 категорій → елементи): шапки й панелі (шапка чату, шапки сторінок, рамка вмісту `NavigationViewContentGridCornerRadius`); пункти налаштувань і картки (пункти, попередній перегляд); кнопки й поля (`ControlCornerRadius`, `GlyphButtonCornerRadius`, поле вводу повідомлення); меню й спливаючі вікна (`OverlayCornerRadius`); капсули в чаті (реакції, `MessageServiceCornerRadius`, `MessageOverlayCornerRadius`). Незмінений вузол наслідує батька (`Radius.<id>` = -1); змінений — окремий; `RadiusSync` задає одне значення всім. Ресурси пише `Theme.UpdateRadii` (у самому словнику теми й у тематичному); визначення з `App.xaml`/`Accent.xaml` прибрані, бо перекривали тему. Е2е: команда драйвера `value <ім'я>`.
- Поле вводу під склом має власну заливку (`InputFill`, «Помітність поля вводу») і обідок у режимах смуги та переходу.
- Насиченість кольорів теми (`ThemeSaturation` 0–150 %, `Theme.Saturate`, яскравість зберігається): кольори теми й відтінки акценту, кольори повідомлень (`MessageStyle.Adjust`), градієнт бульбашок, `SystemAccentColor*`. Кольори імен (профілю) й мініатюри тем не змінюються.
- Скло з переходом над полем вводу — `ProgressiveGlass`: 6 шарів розмиття фону, сила (k/n)², кожен зі своєю маскою-смугою (smoothstep, перекриття у два кроки), розмиття `Quality` (`SolidGaussianBrush.CreateBrush(..., quality: true)`), тон зверху з власною маскою. Окремі `SpriteVisual` з `CompositionMaskBrush`: граф ефектів із градієнтами як джерелами масок компоситор відкидає разом із сусідніми візуалами (зникав увесь чат). Повзунки `InputBlur` (0–200 %) і `InputFadeHeight` (0–160 px, 24 за замовчуванням: розмиття одразу над полем).
- `ContentGlow` перераховує джерела й розміщення після кожного `LayoutUpdated` хоста (а не лише при зміні розмірів): перевикористані під час прокрутки бульбашки й капсули реакцій лишали сяйво попереднього повідомлення там, де були його текст і реакції. `Glow.Place` пропускає незмінене розміщення.
- Сяйво при наведенні: прокрутка колесом виносить елемент з-під нерухомого курсора без PointerExited, і `Hover` лишався 1. Тепер `PointerWheelChanged` гасить, `PointerMoved` знову вмикає; анімація стартує лише при зміні стану.


## Налагодження скла й наведення (2026-10-23)
- Вікно «Налагодження скла» (`Controls/GlassTuner`, окреме вікно через `ViewService.OpenAsync`, `PersistedId` `GlassTuner`): кнопка на сторінці «Вигляд» під налаштуваннями поля вводу. Кожен повзунок одразу пише значення (`GlassTuning.Set`) і подію `GlassTuning.Changed`; `ChatView` перебудовує лише поступове скло (поле вводу — `UpdateInputStyle`, шапка — `AttachFadingHeader`). «Копіювати значення» кладе всі ключі в буфер обміну.
- `GlassTuning`: значення поступового скла для `Input` і `Header`: зсув початку переходу, кількість шарів, степінь кривої сили, перекриття, форма згасання (smoothstep / лінійна / smootherstep / швидкий / повільний спад), насиченість, якісне розмиття, сила й довжина тонування, зсув тонування, межі (жовта — початок переходу, блакитна — тонування, червона — край). Власні значення зберігаються як `Tune.<ключ>`; `InputBlur`, `InputFadeHeight`, `InputFill`, стилі й види скла поля вводу та шапки прив'язані до наявних налаштувань.
- Шапка «Зникаюче скло» тепер теж `ProgressiveGlass` (висота переходу `Header.Fade`, 32 px). Суцільна частина смуги поля вводу рахується від `Footer` (`Footer_SizeChanged`), а не від прихованого поля вводу: у каналі під кнопкою «Сповіщати» смуга брала висоту поля з попереднього чату.
- Наведення: кнопка шапки (`Profile`) у капсулі має відступи й заокруглення капсули (знизу прямі, коли під нею смуги); смуги під шапкою (`ClipperOuter`) обрізані геометричним кліпом по нижніх кутах капсули (радіус мінус 4 px зазору). Новий вузол заокруглень `ListItems` («Пункти списків: чати, папки, таблиці») → ресурс `ListItemCornerRadius` замість жорстких 4 px; `ListRadius.Apply` після зміни заокруглень одразу оновлює рядки списку чатів, папок, таблиць і картки `HeaderedControl` у відкритих вікнах.
- E2E: `hover`/`unhover <x:Name або тип>[#n]` (стан PointerOver без курсора), `tuner [x,y,w,h]` (відкриває на другому моніторі), `tune <ключ> <значення>`.