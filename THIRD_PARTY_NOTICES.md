# Сторонние компоненты / Third-party components

Pulse распространяется вместе со следующими компонентами. Их лицензии разрешают распространение; тексты лицензий — по ссылкам.

| Компонент | Где | Лицензия |
|---|---|---|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 | `lib/` | MPL-2.0 |
| [BlackSharp.Core](https://github.com/Blacktempel/BlackSharp), [DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit), [RAMSPDToolkit-NDD](https://github.com/Blacktempel/RAMSPDToolkit) | `lib/` | MPL-2.0 |
| [HidSharp](https://software.seekye.com/hidsharp) 2.6.4 | `lib/` | Apache-2.0 |
| [Microsoft Edge WebView2 SDK](https://aka.ms/webview) 1.0.4258.31 (`Microsoft.Web.WebView2.*.dll`, `WebView2Loader.dll`) | `lib/` | BSD-3-Clause (WebView2 SDK) |
| System.Buffers, System.CodeDom, System.Memory, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe, System.Security.AccessControl, System.Security.Principal.Windows, System.Threading.AccessControl ([.NET](https://github.com/dotnet)) | `lib/` | MIT |
| [Intel PresentMon](https://github.com/GameTechDev/PresentMon) | `third_party/presentmon/` | MIT (см. `third_party/presentmon/LICENSE.txt`) |
| [Roslyn C# compiler](https://github.com/dotnet/roslyn) (`Microsoft.Net.Compilers.Toolset`, только для сборки, скачивается `tools/restore.ps1`) | `packages/` | MIT |

Не входит в состав, ставится отдельно при включении датчиков: драйвер [PawnIO](https://pawnio.eu/) (через winget или с проверкой SHA-256 официального установщика).

## Логотипы / Logos

Логотипы в `ui/logos/` — товарные знаки их владельцев; используются только чтобы показать, чьё это железо.

- Знаки из [Simple Icons](https://simpleicons.org/) — CC0-1.0.
- Логотипы из [Wikimedia Commons](https://commons.wikimedia.org/) — по лицензиям соответствующих файлов (как правило, общественное достояние как простые текстовые логотипы).
- Для брендов без свободного логотипа — набранные шрифтом надписи (созданы для Pulse).

Logos in `ui/logos/` are trademarks of their respective owners and are used only to identify hardware.
