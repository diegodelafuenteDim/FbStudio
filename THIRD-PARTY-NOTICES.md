# Third-party components

FbStudio uses these NuGet packages as published, unmodified. Each one keeps its own license.

| Package | Used for | License |
|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (Desktop, Skia, Themes.Fluent, Headless) | Cross-platform UI | MIT |
| [Avalonia.AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and AvaloniaEdit.TextMate | SQL editor (and its control styles) | MIT |
| [TextMateSharp.Grammars](https://github.com/danipen/TextMateSharp) | Syntax highlighting | MIT |
| [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) | Visual theme | MIT |
| [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia) | Icons (Material Design Icons) | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM | MIT |
| [Msagl](https://github.com/microsoft/automatic-graph-layout) (Microsoft Automatic Graph Layout) | Diagram layout | MIT |
| [FirebirdSql.Data.FirebirdClient](https://github.com/FirebirdSQL/NETProvider) | Firebird connection | IDPL 1.0 (see below) |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) and SQLitePCLRaw | Local history | MIT / Apache-2.0 |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | Password encryption on Windows (DPAPI) | MIT |
| [SkiaSharp](https://github.com/mono/SkiaSharp) and HarfBuzzSharp | Drawing | MIT |
| ANGLE (Avalonia.Angle.Windows.Natives) | Graphics acceleration on Windows | BSD-3-Clause |
| Tests only: xUnit, Microsoft.NET.Test.Sdk, [Testcontainers](https://github.com/testcontainers/testcontainers-dotnet) | Testing | Apache-2.0 / MIT |

All other indirect dependencies are MIT or Apache-2.0. On Linux, passwords are stored through `secret-tool`
(libsecret, part of the system), which is not shipped with FbStudio.

## FirebirdClient (IDPL 1.0)

The Firebird .NET provider is distributed under the Initial Developer's Public License 1.0 (an MPL-style license).
FbStudio uses it unmodified; its source code is at https://github.com/FirebirdSQL/NETProvider.

## Trademarks

FbStudio is not affiliated with the Firebird Foundation. "Firebird" is a trademark of the Firebird Foundation and is
named for reference only.

---

**En español:** FbStudio usa estos paquetes sin modificarlos y cada uno conserva su licencia; casi todos son MIT o
Apache-2.0. La excepción es **FirebirdClient**, que es IDPL 1.0. FbStudio no está afiliado a la Firebird Foundation.
