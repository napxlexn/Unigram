---
type: Build Guide
title: Локальна збірка форку (Win32, x64)
description: "Як зібрати й запустити форк на цій машині, і які обхідні шляхи потрібні поверх upstream develop станом на 2026-10-20."
timestamp: "2026-10-20T00:00:00+00:00"
---

# Локальна збірка форку

Гілка `local-build` = `feature/transparency` + обхідні шляхи, без яких upstream `develop`
(c4c8c71ca, 2026-10-20) не збирається й не запускається.

## Інструменти
- VS Community 2026 (18.11) у `D:\Microsoft Visual Studio\18\Community`: компоненти з `.vsconfig`, плюс Windows11SDK.26100 і Vcpkg.
- CMake 4.4.4 у `C:\Program Files\CMake\bin`. Його треба ставити першим у PATH, бо Strawberry Perl підсовує свій CMake 3.29.
- PHP 8.5 (у winget версії 8.3 і 8.4 віддають 404) і `gperf` (`oss-winget.gperf`), потрібні для генерації коду TDLib.
- Rust 1.96.1: rustup ставить його сам за `rust-toolchain.toml` у wallet-engine.
- Увімкнено режим розробника Windows (`AppModelUnlock\AllowDevelopmentWithoutDevLicense=1`) і встановлено фреймворк `Microsoft.VCLibs.140.00.Debug` (x64 appx з ExtensionSDKs).

## Обхідні шляхи поверх upstream
1. **tgcalls.** Коміти `185fa733d`, `d5b434dc0` і `71f4b8b1f` посилаються на неопублікований коміт tgcalls `cb12fe31…`, тож вони відкочені. Коли автор опублікує tgcalls, ці відкати можна прибрати.
2. **Прибрані `using`.** `774ed57d5 Code cleanup` прибрав `using`, які ще потрібні, і їх повернуто (Utils.cs, Composition/Extensions.cs, FormattedTextBlock.cs).
3. **Подвійний VCLibs у маніфесті.** VS 18.11 сам додає `PackageDependency` на VCLibs, тому XSLT `Package.Win32.Final.xslt` додає його лише тоді, коли залежності ще немає.
4. **Розширений TDLib API.** `Telegram/Td/Client.cs` викликає `td_send(client_id, request_id, request)` і `td_receive(timeout, &client_id, &request_id)`. Це приватний патч автора, якого немає в жодному публічному форку. Його відтворює `Libraries/tdjson/unigram-request-id.patch`, і `build.ps1` накладає патч автоматично. Без нього застосунок падає в `strlen` усередині `td_send`.
5. **wallet-engine.** `wallet_engine.dll` не входить у репозиторій. Його збирають з `i582/wallet-engine` на коміті `e59e0d8` (з нього згенеровано біндинги) командою `cargo build --locked --release --target x86_64-pc-windows-msvc`. Клон лежить у `D:\Unigram-deps\wallet-engine`, результат кладуть у `Libraries/wallet-engine/x64/`.

## Кроки
1. `git submodule update --init --recursive`
2. `Telegram/Constants.Secret.cs` (у .gitignore): ApiId/ApiHash з налаштувань Telegram-MCP.
3. Нативні частини: `MSBuild Telegram.Win32.slnx -target:Telegram_Native;Telegram_Native_Calls -restore -p:Configuration=Debug -p:Platform=x64 -p:RestorePackagesConfig=true` (~20 хв уперше).
4. TDLib: з `Libraries\tdjson` запустити `powershell -NoProfile -File build.ps1 -arch x64`.
5. Застосунок: `MSBuild Telegram\Telegram.Win32.csproj -restore -p:Configuration=Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true`.
6. `Add-AppxPackage -Register <bin\win32\x64\Debug\...\win-x64>\AppxManifest.xml`, потім запуск через `shell:AppsFolder\38833FF26BA1D.UnigramWin32_g9c9v27vpyspw!App`.

Цей пакет встановлюється поруч з основним Unigram і не конфліктує з ним: має окрему ідентичність і протокол `tg-win32`.
