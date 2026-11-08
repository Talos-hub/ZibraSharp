# ZibraSharp

Console utility that scans a directory, packs its files into a ZIP archive, and uploads the archive to Google Drive.

It is a C#/.NET 9 rewrite of the original Go tool. The archive format is compatible with the standard ZIP format (including ZIP64 and UTF-8 file names), so archives can be opened by any ZIP tool.

## Features

- Recursive directory scan with optional extension exclusions.
- Real parallel compression: files are deflated by a worker pool, then assembled by a single writer.
- ZIP64 support and UTF-8 entry names (Windows and Unix).
- Atomic archive replacement: a new archive is written to a temporary file and moved into place only after it is complete.
- Offline-friendly upload callback: if upload fails, the archive is kept and queued; the next run offers to upload it.
- Structured JSON-lines logging with size-based rotation.
- Atomic JSON writes for configuration and queue state.
- Dry run, configurable worker count, overwrite protection, and Ctrl+C cancellation.
- BenchmarkDotNet suite and xUnit test suite.

## Requirements

- .NET 9 SDK or runtime.
- A Google Cloud OAuth client with the Google Drive API enabled.

## Setup

1. Create OAuth credentials (Desktop application) in Google Cloud Console and download the JSON file.
2. Put the credentials file in one of these locations:
   - `credentials.json` in the current working directory;
   - the path from `CREDENTIALS_ZIBRA` or `CREDENTIALS_ZIBRASHARP`;
   - `config/credentials.json` or `configuration/credentials.json` in the current working directory;
   - `~/.ZibraSharp/credentials.json`.
3. Configure the directory where archives are stored:

   ```bash
   zibrasharp settings /path/to/backups
   ```

   Or run `zibrasharp settings` without arguments for an interactive prompt.

4. On the first upload, a browser window opens for OAuth consent. The token is stored in `token.json` next to the working directory. On Unix the token file is restricted to the owner (`0600`).

## Usage

```text
zibrasharp start [archive.zip] <directory> [options]
zibrasharp settings [<path>] [--show]
zibrasharp extensions [<extensions...>] [--list] [--remove]
zibrasharp log [--lines N]
zibrasharp pending [upload]
zibrasharp version | --version
zibrasharp help
```

### `start`

Creates an archive from `<directory>` and uploads it.

- With one positional argument (`start <directory>`) the archive name is generated automatically: `<directory-name>-yyyyMMdd-HHmmss.zip`.
- With two positional arguments (`start <archive.zip> <directory>`) the archive name is explicit.

Options:

| Option | Description |
|---|---|
| `--yes`, `-y` | Upload pending archives without asking. |
| `--dry-run` | Scan and report file count and size without creating an archive. Does not require configuration or credentials. |
| `--force`, `-f` | Replace an existing archive. Without it, `start` refuses to overwrite. |
| `--keep-archive` | Keep the archive on disk after a successful upload. |
| `--workers N` | Number of compression workers. `0` (default) means one per CPU core. |

### `settings`

- `settings` — interactive prompt for the backup directory.
- `settings <path>` — save the backup directory non-interactively.
- `settings --show` — print the current backup directory and excluded extensions.

### `extensions`

- `extensions` — interactive prompt (space-separated list, for example `.exe .txt .json`).
- `extensions .txt .exe` — save the list non-interactively.
- `extensions --list` — print the current exclusions.
- `extensions --remove` — delete the extensions file.

Extensions are *excluded* from scanning. Files without an extension are always included.

### `log`

Prints the last 50 JSON log lines by default:

```bash
zibrasharp log --lines 200
```

### `pending`

- `pending` — list archives waiting for upload.
- `pending upload` — retry every queued archive.

### Global options

| Option | Description |
|---|---|
| `--verbose` | Enable debug logging. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Configuration, validation, or unexpected error. |
| `2` | Archive created, but upload failed; the archive is kept and queued. |
| `130` | Cancelled with Ctrl+C. |

## Files

All runtime files live in the current working directory unless noted otherwise:

| File | Purpose |
|---|---|
| `zipdir.json` | Backup directory (`{"zipdir":"..."}`). |
| `extensions.json` | Excluded extensions (`{".exe":true}`). Legacy `extentions.json` is read as a fallback. |
| `pending.json` | Queue of archives waiting for upload. |
| `token.json` | Google OAuth token. |
| `zibra.log` | JSON-lines log. Rotated to `zibra.1.log`, `zibra.2.log`, … |
| `credentials.json` | Google OAuth client (never committed). |

`credentials.json`, `token.json`, `zipdir.json`, `extensions.json`, `pending.json`, `*.zip`, and `*.log` are ignored by Git.

## Offline behavior

`LazyGoogleDriveApi` initializes OAuth and the network client only when an upload starts. Therefore:

1. The archive is always created first.
2. If the cloud is unreachable, the archive is **not** deleted.
3. Its path is written to `pending.json`.
4. The next `start` (or `pending upload`) retries the upload.

This prevents data loss when the network is unavailable.

## Logging

The log is JSON-lines, one event per line:

```json
{"time":"2026-11-08T12:00:00.0000000+00:00","level":"INFO","msg":"Files found","Count":128}
```

- Levels: `DEBUG`, `INFO`, `WARN`, `ERROR`.
- Errors include the exception type, message, stack trace, and inner exceptions.
- The log rotates at 10 MB and keeps 5 rotated files by default.
- The file is created lazily on the first event.

## Development

Build:

```bash
dotnet build ZibraSharp/ZibraSharp.sln
```

Run tests:

```bash
dotnet test ZibraSharp.Tests/ZibraSharp.Tests.csproj
```

Run benchmarks:

```bash
dotnet run --project ZibraSharp.Benchmarks/ZibraSharp.Benchmarks.csproj -c Release
```

List available benchmarks:

```bash
dotnet run --project ZibraSharp.Benchmarks/ZibraSharp.Benchmarks.csproj -c Release -- --list flat
```

Run a specific suite:

```bash
dotnet run --project ZibraSharp.Benchmarks/ZibraSharp.Benchmarks.csproj -c Release -- --filter *ZipArchiverBenchmarks*
```

## Project structure

```text
ZibraSharp/
  CLI/                     Command-line entry point and argument parsing
  Internal/
    Api/                   Google Drive client, credentials, token store
    Archivers/             Parallel ZIP writer and CRC32
    Config/                zipdir.json and extensions.json
    Exceptions/            AppException and error codes
    IO/                    Atomic file writes
    Logger/                JSON-lines logger with rotation
    Ports/                 Interfaces and shared records
    Services/              Backup flow, pending uploads, queue store
    Walkers/               Recursive directory scanner
ZibraSharp.Tests/          xUnit tests
ZibraSharp.Benchmarks/     BenchmarkDotNet benchmarks
```

## Security notes

- Never commit `credentials.json` or `token.json`.
- The OAuth token is a secret; on Unix it is written with owner-only permissions.
- Archives are uploaded to the Drive account that authorized the token. Verify the account before the first production run.
