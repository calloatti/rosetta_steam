# Timberborn Rosetta Generator

**Timberborn Rosetta Generator** is an automated C# command-line utility designed to crawl the Steam Workshop for Timberborn mods, anonymously download their `manifest.json` files, and generate a unified mapping database (`rosetta.txt`). 

It connects Steam Workshop `PublishedFileID` numbers directly to internal Timberborn Mod IDs, versions, game compatibility targets, and mod dependencies, while also flagging mod ID conflicts and misconfigured mod packages.

---

## Features

- **Anonymous Workshop Crawler:** Uses SteamKit2 to connect to Steam CM servers anonymously and retrieve public Timberborn Workshop mod metadata (App ID `1062090`).
- **Parallel Manifest Downloader:** Spawns multi-threaded worker sessions to fetch only `manifest.json` files directly from Steam Depots without requiring user authentication or full mod downloads.
- **Rosetta Database Exporter:** Compiles all mod metadata into a tab-separated values (`rosetta.txt`) report for easy parsing and database integration.
- **Conflict & Sanity Analyzer:**
  - `rosetta_duplicates.txt`: Identifies separate Workshop items that use conflicting internal Mod IDs.
  - `rosetta_invalid_packages.txt`: Identifies single Workshop items that improperly package multiple distinct internal Mod IDs across subfolders.

---

## Output Files

| File / Directory | Description |
| :--- | :--- |
| `rosetta.txt` | Primary TSV export linking `PublishedFileID`, internal `Id`, folder names, `Name`, Workshop `Title`, `Version`, `MinimumGameVersion`, and required/optional mod dependencies. |
| `rosetta_duplicates.txt` | Log of internal Mod IDs that conflict across different Steam Workshop items. |
| `rosetta_invalid_packages.txt` | Log of Workshop items containing multiple separate internal Mod IDs. |
| `RosettaGenerator.log` | Detailed log of network connections, crawling progress, downloads, and export stages. |
| `data/` | Local directory storing downloaded `manifest.json` files organized by `PublishedFileID`. |
| `accounts/` | Directory storing anonymous Steam worker session configurations. |
| `steam_appid.txt` | Configuration file storing the target Steam App ID (defaults to `1062090`). |

---

## How It Works

1. **Initialization:** Checks for `steam_appid.txt` in the base directory (creates it with `1062090` if missing).
2. **Crawl Phase (`SteamCrawlerService`):** Connects to Steam CM servers anonymously, executing `PublishedFile.QueryFiles` queries page-by-page to fetch public mod IDs and metadata.
3. **Download Phase (`ModDownloaderService`):** Spawns 4 concurrent anonymous Steam client sessions to download `manifest.json` files into the `./data/{PublishedFileID}/` directories.
4. **Export & Analysis Phase (`ManifestProcessorService`):** Reads downloaded manifests, sanitizes string fields, constructs the relational `rosetta.txt` file, and executes duplicate/invalid package checks.

---

## Requirements & Setup

### Prerequisites
- .NET 10.0 SDK / Runtime (or compatible .NET runtime)
- Active internet connection to reach Steam CM servers

### Running the Utility
1. Place the compiled binary in a directory.
2. Run `TimberbornRosettaGenerator.exe`.
3. The utility will automatically generate configuration files, execute the pipeline, and output the generated reports in the executable root folder.