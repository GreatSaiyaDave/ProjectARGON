# Vendored SQLite for the OCG lab catalog

NuGet closure of **Microsoft.Data.Sqlite 8.0.11** (MIT) as actually restored:

- `Microsoft.Data.Sqlite.Core` 8.0.11 (`netstandard2.0`)
- `SQLitePCLRaw.core` 2.1.6
- `SQLitePCLRaw.provider.e_sqlite3` 2.1.6
- `SQLitePCLRaw.batteries_v2` 2.1.6
- `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 natives: `Assets/Plugins/x86_64/libe_sqlite3.so` (linux-x64), `win-x64/e_sqlite3.dll`

This is not AGPL. Call `SQLitePCL.Batteries_V2.Init()` once before opening `cards.cdb`.
