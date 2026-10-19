# Сторонние компоненты

Исходный код Scan on WIN распространяется по MIT. Сторонние библиотеки сохраняют собственные лицензии; тексты включены в `licenses/` готовой сборки. Платных библиотек нет.

Архив `ScanOnWIN-Source.zip`, поставляемый рядом с установщиком, включает полные соответствующие исходники NAPS2 в `third-party-sources/`. При передаче установщика другим пользователям передавайте и архив исходников. Способ получения и проверка SHA-256 закреплены в `scripts/Get-UpstreamSource.ps1`.

- NAPS2 SDK / Images / Escl / Internals / Win32 worker 1.3.0 — NAPS2 Contributors, LGPL-2.1-or-later. Неизменённые динамические библиотеки можно заменить совместимыми сборками. Исходники: https://github.com/cyanfish/naps2/tree/8ae3e82203115754e804fe9c14f00f6bd86ee192 ; лицензия в `LICENSE-NAPS2.txt`.
- NAPS2.Tesseract.Binaries 1.4.0 — Tesseract и Leptonica, Apache-2.0 / BSD; бинарный пакет и сведения об исходниках: https://github.com/cyanfish/naps2-tesseract ; https://github.com/tesseract-ocr/tesseract . Модели `tessdata_fast` rus/eng — Apache-2.0, https://github.com/tesseract-ocr/tessdata_fast/tree/87416418657359cb625c412a48b6e1d6d41c29bd .
- NAPS2.NTwain, NAPS2.PdfSharp и NAPS2.Wia — лицензии включены из пакетов NuGet, исходники и точные версии перечислены в `DEPENDENCIES.json`.
- TWAIN DSM x86/x64 — TWAIN Working Group, LGPL-2.1; библиотека диспетчера источников, не драйвер устройства. Бинарные файлы взяты из той же закреплённой ревизии NAPS2 (`NAPS2.Setup/lib/win32` и `win64`). Лицензия: `licenses/LICENSE-TWAIN-DSM.txt`, исходники диспетчера: https://github.com/twain/twain-dsm .
- Остальные транзитивные зависимости и .NET runtime: точные версии, лицензии и ссылки приведены в `DEPENDENCIES.json`, `licenses/` и `ThirdPartyNotices.txt` среды .NET.

Scan on WIN — самостоятельное приложение на основе NAPS2 SDK; не официальный выпуск NAPS2. Драйверы производителей не включены. Установщик собирается с Inno Setup, https://jrsoftware.org/isinfo.php .
