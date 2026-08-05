# AGENTS.md

## Project overview

TimberbornRosettaGenerator is a .NET 10 console application (C#, namespace `TimberbornRosettaGenerator`) that builds a "Rosetta" database for the game Timberborn. It crawls the Steam Workshop for all public Timberborn mods, downloads each mod's `manifest.json` anonymously via the SteamDepotDownload library, and exports tab-separated reference files that map Steam Workshop `PublishedFileID`s to internal mod IDs, names, versions, and mod dependencies.

The output (`rosetta.txt`) is a flat TSV that links Workshop items to the mod loader's internal IDs so Workshop mods can be matched/resolved at runtime.

## Pipeline (entry point: `Main.cs`)

1. `SteamCrawlerService.VerifyConfiguration()` — ensures `steam_appid.txt` exists (writes default appid `1062090`) and reads it. No Web API key is required.
2. `SteamCrawlerService.GetAllPublicModIdsAsync()` — opens one anonymous CM session and paginates the `PublishedFile.QueryFiles#1` unified message (`query_type=21`, tag `Mod`, excluding `BuildingBlueprints`, `return_details=true`), collecting all published file IDs and building the in-memory `SteamModLookup` (`Dictionary<string, SteamModMetadata>` mapping published file ID → Title/Creator-SteamID64).
3. `ModDownloaderService.ProcessModsAsync()` — spawns 4 anonymous Steam network sessions (distinct `LoginId`s + per-worker `AccountStore`), and for every mod downloads only files matching `regex:.*manifest\.json` into `data/<id>/` via `DownloadPubfileAsync` with `VerifyAll = true`.
4. `ManifestProcessorService.RunExport()` — parses every cached `manifest.json`, sanitizes fields, writes `rosetta.txt` plus the analysis files.
5. `LogService` is used throughout for console + file logging.

No Steam client or Web API key is required; enumeration and downloads use anonymous SteamKit connections directly. The crawler must register a `SteamUnifiedMessages.UnifiedService` (`PublishedFileService`) keyed on `"PublishedFile"` before sending — without it, SteamKit2 3.4.0 silently drops `QueryFiles` responses and the job times out.

## Services

| File | Responsibility |
| --- | --- |
| `Main.cs` | Entry point and pipeline orchestration. |
| `SteamCrawlerService.cs` | Keyless anonymous CM Workshop crawl; owns `SteamModLookup`. |
| `ModDownloaderService.cs` | Anonymous worker-pool manifest downloader (SteamDepotDownload). |
| `ManifestProcessorService.cs` | Manifest parsing, sanitizing, TSV export, duplicate/invalid analysis. |
| `LogService.cs` | Thread-safe console + `RosettaGenerator.log` logging. |

## Data models (`ManifestProcessorService.cs`)

- `ModDependency` — `Id`, `MinimumVersion`.
- `RosettaData` — `Id`, `Name`, `Version`, `MinimumGameVersion`, `RequiredMods`, `OptionalMods` (deserialized case-insensitively with trailing commas and comments tolerated; falls back to regex extraction on parse failure).
- `SteamModMetadata` (`SteamCrawlerService.cs`) — `Title`, `Creator`.

## Output files (written to the executable directory)

| File | Contents |
| --- | --- |
| `rosetta.txt` | Main TSV: `PublishedFileID, Id, Directory_Name, Name, Title, Version, MinimumGameVersion, RequiredMods_Id, RequiredMods_MinimumVersion, OptionalMods_Id, OptionalMods_MinimumVersion` (one row per dependency). |
| `rosetta_duplicates.txt` | Same internal mod `Id` appearing under multiple Workshop items. |
| `rosetta_invalid_packages.txt` | One Workshop item containing multiple distinct mod `Id`s. |
| `RosettaGenerator.log` | Session log (cleared on each run). |
| `data/<PublishedFileID>/` | Cached `manifest.json` files downloaded from Workshop mods. |
| `data/<PublishedFileID>/.sdd/` | SteamDepotDownload state store per mod (depot manifests + install record). MUST NOT be deleted — it enables the 0-byte "unchanged" fast path. |
| `accounts/worker-<N>.config` | Per-worker anonymous session account stores (created at runtime). |

## Input/config files

- `steam_appid.txt` — Timberborn app id (default `1062090`).
- `rosetta_spec.txt` — example of expected rosetta line format (not used at runtime; copied to output for reference).

## Build & run

```
dotnet build
dotnet run
```

- Target: `net10.0` (x64), `ImplicitUsings` and `Nullable` enabled.
- NuGet dependencies: `SteamDepotDownload` (1.0.4, GPL-3.0) and `SteamKit2` (3.4.0, used directly by the crawler). Transitive deps: protobuf-net, ZstdSharp, Spectre.Console, QRCoder.
- `steam_appid.txt` is copied to the output directory.

## Known issues / gotchas

- `ModDownloaderService` hardcodes app id `"1062090"` for Workshop/UGC operations while `SteamCrawlerService` reads it from `steam_appid.txt`. Keep them in sync if the target game changes.
- The crawler's `Creator` metadata is the numeric SteamID64, not a profile name (anonymous CM queries cannot resolve persona names).
- Export only processes folders whose ID appears in the current crawl response; cached folders not in the crawl are preserved on disk but skipped (see the bypass log message).
- Root-level `manifest.json` is skipped when a mod folder also contains subfolder manifests (only the version-specific manifests are exported).
- Single-file web-hosted Workshop items contain no `manifest.json`; they still succeed as a 0-file download and are simply absent from the export.
- Under `VerifyAll = true` the library's `AlreadyInstalled` flag is never set; an "unchanged" mod is recognized by 0 bytes downloaded / 0 files downloaded.
- `bin/` and `obj/` are generated artifacts; avoid committing them.
- Windows-only runtime paths; other platforms are best-effort.
