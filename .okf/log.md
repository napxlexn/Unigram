# Directory Update Log

## 2026-10-24
* **update**: Rim smoothing 'Precise' (Direct2D coverage, gamma-aware) is the new default; found that CompositionVisualSurface renders shapes without AA. Fixed 11 review findings (bot keyboard under glass, double channel-footer glass, capsule header clip, hit-test beside capsule composer, composer clip radius, header/bubble theme refresh, banner glass, bar corners, UWP material picker, open-without-reading on back nav, tuner labels)
* **update**: WebView DevTools default off (was on in Debug builds, opened DevTools with every video); opt-in via Diagnostics
* **update**: Composer style option labels shortened (Скляна капсула / Скляна смуга / Скло з переходом) so the combo no longer overlaps the row title
* **update**: Власні іконки налаштувань скла; демонстрація пакетів іконок
* **update**: Колонка повідомлень під прямим краєм капсули поля вводу
* **update**: Повідомлення під скляною капсулою поля вводу й кнопки сповіщень
* **update**: Рядки капсули шапки однаковими пілюлями, кнопки вирівняно; фон вікна налагодження

## 2026-10-23
* **update**: Вікно налагодження скла, поступове скло шапки, наведення за формою капсули й живі заокруглення списків

## 2026-10-21
* **fix**: Сяйво при наведенні гасне при прокрутці
* **fix**: Застаріле сяйво в перевикористаних бульбашках
* **update**: Перехід розмиття над полем вводу опущено до 24 px
* **update**: Прогресивне розмиття над полем вводу
* **update**: Дерево заокруглень, видиме поле вводу, насиченість теми
* **update**: Скло поля вводу та інших полів, шапка-капсула без вмісту над нею, відступи шапки, заокруглення
* **update**: Окремий обідок для повідомлень, ореол ShadowCaster, відступи, сяйво реакцій, повзунки сяйва
* **update**: Повзунки сили ефектів; експериментальні способи згладжування обідка
* **update**: Анімації обідка, реакція на курсор, закріплений перегляд із перемикачем матовості
* **update**: Обідок: колір, світло, анімація, товщина, де показувати; виправлено ореол бульбашок
* **update**: Майданчик прибрано; налаштування повідомлень перенесено під стандартний перегляд
* **update**: transparency: unified GlassKind, rim styles, effect strengths, capsule header as one element, open without reading
* **update**: transparency: glass chat header, separator modes, content glow, SmoothGlass; local-build: Primary identity and data import
* **update**: transparency: header styles, own message colours, layered footer, RO_E_CLOSED fix

## 2026-10-20
* **update**: Скляні стилі повідомлень, шапка з плавним переходом, читання без позначки
* **update**: Матеріали: прибрано Mica, додано матеріал бічної панелі, папки, розділювачі, щільний футер; імпорт теми з оригіналу; HandleCollector, лог TDLib і кеш .tgfc
* **update**: Прозорість: єдиний шар тінту, вимикач шпалер, виправлено blur-behind і шпалери на 0 %; e2e-режим --no-activate
* **update**: Локальна збірка Win32 працює: патч TDLib request_id, wallet-engine, VCLibs у маніфесті, відкат неопублікованого tgcalls
* **add**: Форк napxlexn/Unigram: додано функцію прозорості вікна (матеріал + повзунки фону та шпалер)
* **Initialization**: Scaffolded OKF bundle for Unigram
