# <img src="assets/NSS-128x128.png" align="left" /> AppVeyor (and GitHub!) Artifacts Receiver

[![Docker Image CI](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%209-blue.svg)

A web service that listens for deployment webhook calls from [AppVeyor](https://www.appveyor.com/) (and GitHub actions) CI/CD and mirrors build artifacts to your local file system.

## About

This project hosts a webhook server that you can point an AppVeyor deployment to. When new builds complete, it automatically downloads and stores the artifacts locally, bypassing AppVeyor's retention limits.

## Features

- **Artifact mirroring** — Store build artifacts on your own infrastructure to circumvent the one-month retention policy.
- **Fast deployment completion** — The server initiates artifact downloads asynchronously, so the deployment step finishes quickly and successfully. With other methods, network hiccups often cause deployments to fail and require manual retries.
- **Latest symlink** — When configured, a symbolic link is created or updated at the path given by `LatestSymlinkTemplate`, pointing at the directory for the current build (derived from `TargetPathTemplate`). Use this for a stable URL to the newest artifacts.
- **Latest timestamp file** — When `TargetPathTemplate` is set, `LAST_UPDATED_AT.txt` is written in the build directory with the ISO 8601 timestamp when each deployment completes, for APIs or scripts to consume (independent of whether `LatestSymlinkTemplate` is configured).
- **SVG badge** — `LAST_UPDATED_AT.svg` is generated alongside the timestamp file under the same rules.
- **Executable metadata** — When `StoreMetaData` is enabled, Win32 version resource data (`FileVersion`, `ProductVersion`) is extracted from PE files (`.exe`, `.dll`, and similar) and written to hidden sidecar JSON next to the file (e.g. `.MyApp.exe.json`) for auto-updaters and other tools.
- **ZIP artifact metadata** — If the downloaded artifact is a ZIP, the same metadata extraction runs over entries inside the archive: entries are scanned up to a configurable limit, oversized entries are skipped, paths are validated (including zip-slip checks), and PEs without a typical extension are detected via the MZ header. Sidecars are stored under a hidden tree rooted at `.{sanitized_zip_basename}/`, mirroring the in-archive path (each directory segment is stored as a hidden segment; each file gets a `.filename.json` sidecar in the corresponding mirrored folder).

## Configuration reference

Settings live under `ServiceConfig:Webhooks` in `appsettings` (see [src/appsettings.Production.example.json](src/appsettings.Production.example.json)). Each webhook is keyed by a GUID string matching the URL path `/webhooks/{Id}`.

| Property | Description |
| -------- | ----------- |
| `TargetPathTemplate` | **Required.** Subdirectory under `RootDirectory` for this build. Use `{placeholder}` tokens; values are taken from the webhook JSON `environmentVariables` object. An unknown placeholder **fails** the request. |
| `LatestSymlinkTemplate` | Optional. Set this only if you want a `latest`-style symlink: after a successful deployment, the symlink at the expanded path is updated to point at the current build directory (same `{placeholder}` rules as `TargetPathTemplate`). Omit it if you do not need that indirection. |
| `RootDirectory` | **Required.** Root folder on disk where build trees and metadata are stored (e.g. `/data` in Docker). |
| `StoreMetaData` | Optional; default `true`. Set `false` to skip PE metadata sidecars for both loose PE files and ZIP contents. |
| `ZipMaxEntriesToScan` | Optional. Maximum ZIP entries examined per artifact for PE metadata. Use `0` for the built-in default (**8192**). |
| `ZipMaxEntryBytes` | Optional. Maximum uncompressed size in bytes of a single ZIP entry to load for parsing. Use `0` for the built-in default (**256 MiB**). |

**Path safety:** The service combines `RootDirectory` with the expanded `TargetPathTemplate` and `LatestSymlinkTemplate` paths using `Path.Combine`. It does **not** re-check that the result stays inside `RootDirectory`. If placeholder values (from webhook `environmentVariables`) can contain `..`, absolute paths, or other traversal segments, the resolved path may escape the intended root—affecting creation of build directories, artifact writes, PE sidecars, the latest symlink target, and `LAST_UPDATED_AT.txt` / `LAST_UPDATED_AT.svg` under the build directory. Validate or sanitize those variables at the source (CI payload) so expanded templates resolve strictly under `RootDirectory`.

## Quick Start

### Running with Docker

The image [Dockerfile](Dockerfile) exposes port **8080** by default for the base ASP.NET layer; in practice you configure the listen URL in your mounted `appsettings.Production.json` (the examples use **7089**). Map the host port to whatever port the app binds to inside the container.

```bash
docker build -t appveyor-artifacts-receiver .
docker run -d -p 7089:7089 \
  -v /path/to/data:/data \
  -v /path/to/appsettings.Production.json:/app/appsettings.Production.json:ro \
  appveyor-artifacts-receiver
```

See [docker-compose.example.yml](docker-compose.example.yml) for a full compose example.

### Configuration

1. Log into AppVeyor and [create a new deployment](https://ci.appveyor.com/environments/new) with the **Webhook** provider.
2. Specify the URL where you host the service (e.g. `https://ci.example.org/webhooks/7b544703-bdd0-4420-9b96-18208076d4df`).
   - **Important:** Use a new, auto-generated GUID and keep it secret.
3. Copy [src/appsettings.Production.example.json](src/appsettings.Production.example.json) to `appsettings.Production.json`. Then tune `Kestrel` and `ServiceConfig:Webhooks` for your environment—use the same webhook GUID as in the deployment URL, and keep or drop `LatestSymlinkTemplate` depending on whether you want the latest symlink (timestamp and badge files still apply whenever `TargetPathTemplate` is configured).

Once running, the service listens for webhook requests containing artifact URLs to download.

### AppVeyor Configuration

Add the following to your `appveyor.yml`:

```yml
deploy:
  - provider: Environment
    name: BUILDBOT
    on:
      appveyor_repo_tag: true
```

## GitHub Actions Support

The same server can receive webhooks from GitHub Actions with a compatible payload. The `artifacts` array may contain **multiple** entries; each is downloaded in turn.

When artifact URLs are GitHub Actions `archive_download_url` values, the requester must send the same token the workflow uses for the API in the **`X-GitHub-Token`** header. The receiver attaches it as a Bearer token for the download and, when this header is present, **waits until processing finishes** before responding with `OK`, so short-lived tokens remain valid for the actual HTTP GET.

This repository ships a reusable composite action that does that work for you: it lists artifacts from the **current** workflow run, builds the compatible payload, and POSTs it to your receiver. Store the webhook URL (including the secret GUID path) as `WEBHOOK_URL`. The calling job needs `actions: read` so the action can list artifacts, and you must **upload artifacts before** invoking it.

Pin `uses:` to a tag or commit in production if you do not want to follow `master`.

### Example workflow

```yml
name: Build and Upload to Buildbot

on:
  push:
    tags:
      - "v*"   # Only run when pushing tags that start with 'v'

permissions:
  contents: read
  actions: read   # Required to list artifacts via the API

jobs:
  build:
    runs-on: windows-latest

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Setup .NET 9 SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Publish .NET 9 Desktop App
        run: dotnet publish --configuration Release -o publish

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: ${{ github.event.repository.name }}
          path: publish/**

      - name: Notify artifacts receiver
        uses: nefarius/AppVeyorArtifactsReceiver@master
        with:
          webhook-url: ${{ secrets.WEBHOOK_URL }}
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

By default the action sends **every** non-expired artifact from the run. To send only one, set `artifact-name` to the exact `actions/upload-artifact` name:

```yml
      - name: Notify artifacts receiver
        uses: nefarius/AppVeyorArtifactsReceiver@master
        with:
          webhook-url: ${{ secrets.WEBHOOK_URL }}
          github-token: ${{ secrets.GITHUB_TOKEN }}
          artifact-name: ${{ github.event.repository.name }}
```

## Third-Party Credits

- [Polly](https://github.com/App-vNext/Polly)
- [PeNet](https://github.com/secana/PeNet)
- [Serilog](https://serilog.net/)
- [FastEndpoints](https://github.com/FastEndpoints/FastEndpoints)
- [Serilog.Enrichers.Sensitive](https://github.com/serilog-contrib/Serilog.Enrichers.Sensitive)
- [Nefarius.Utilities.AspNetCore](https://github.com/nefarius/Nefarius.Utilities.AspNetCore)
