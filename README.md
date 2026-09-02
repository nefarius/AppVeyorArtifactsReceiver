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

### Path placeholders

`TargetPathTemplate` and `LatestSymlinkTemplate` expand `{placeholder}` tokens from the webhook JSON `environmentVariables` object. The receiver does not invent values: AppVeyor sends its deployment environment variables, and the bundled GitHub Action synthesizes the catalog below. An unknown placeholder **fails** processing for that request.

Token names must match `[a-z_][a-z0-9_]*` (letters, digits, underscore; must start with a letter or underscore). Matching is case-insensitive, but lookup uses the key as written in the template against the payload keys, so prefer the lowercase names in this document.

**Path safety:** The service combines `RootDirectory` with the expanded `TargetPathTemplate` and `LatestSymlinkTemplate` paths using `Path.Combine`. It does **not** re-check that the result stays inside `RootDirectory`, and placeholder values are **not sanitized**. Refs, workflow names, and repository slugs can contain `/` or other path separators; values may also contain `..` or absolute segments. Either case can create extra directories or escape the intended root—affecting build directories, artifact writes, PE sidecars, the latest symlink, and `LAST_UPDATED_AT.txt` / `LAST_UPDATED_AT.svg`. Validate or sanitize those variables at the source (CI payload) so expanded templates resolve strictly under `RootDirectory`. Prefer tokens that are single path segments (`github_repository_name`, `github_ref_name`, run numbers) over values that embed slashes (`github_repository`, `github_ref`).

#### AppVeyor

AppVeyor webhook deployments include the build's environment variables. Any of those keys can be used as `{placeholder}` tokens. Names are typically lowercase in the payload (for example `appveyor_build_version`, not `APPVEYOR_BUILD_VERSION`).

Common variables used in path templates:

| Placeholder | Typical meaning |
| ----------- | --------------- |
| `{appveyor_project_name}` | Project display name |
| `{appveyor_project_slug}` | Project slug from the AppVeyor URL |
| `{appveyor_repo_name}` | Repository as `owner-name/repo-name` (contains `/`) |
| `{appveyor_repo_branch}` | Branch being built (for PRs: the base branch) |
| `{appveyor_repo_tag_name}` | Tag name when the build was started by a tag |
| `{appveyor_build_version}` | Build version (unique per project when AppVeyor versioning is used) |
| `{appveyor_build_number}` | Incremental build number |
| `{appveyor_build_id}` | Unique AppVeyor build ID |
| `{appveyor_repo_commit}` | Commit SHA |

See AppVeyor's [environment variables](https://www.appveyor.com/docs/environment-variables/) for the full catalog, including commit author, PR, and job fields.

Example (also in [src/appsettings.Production.example.json](src/appsettings.Production.example.json)):

```json
"TargetPathTemplate": "builds/{appveyor_project_name}/{appveyor_repo_branch}/{appveyor_build_version}",
"LatestSymlinkTemplate": "builds/{appveyor_project_name}/latest"
```

#### GitHub Actions

The bundled action always sends the three AppVeyor-compatible aliases **and** a native `github_*` catalog. Existing AppVeyor-oriented templates keep working; new GitHub-only templates should use the native names.

**Compatibility aliases** (unchanged; not unique per GitHub run):

| Placeholder | Source |
| ----------- | ------ |
| `{appveyor_project_name}` | Repository name (`DsHidMini`) |
| `{appveyor_repo_branch}` | `GITHUB_REF_NAME` (branch or tag name) |
| `{appveyor_build_version}` | Same as `{appveyor_repo_branch}` — **not** a GitHub run number |

Using the AppVeyor example template against GitHub therefore stores artifacts under `builds/DsHidMini/master/master`. Every later push or rerun of `master` overwrites that directory and retargets `latest` at it.

**Native GitHub placeholders** synthesized by [action.yml](action.yml):

| Placeholder | Source | Notes |
| ----------- | ------ | ----- |
| `{github_repository_owner}` | `context.repo.owner` | Owner or org login |
| `{github_repository_name}` | Repository name | Single path segment; prefer this over `{github_repository}` |
| `{github_repository}` | `GITHUB_REPOSITORY` | `owner/name` — contains `/` |
| `{github_ref}` | `GITHUB_REF` | e.g. `refs/heads/master` — contains `/` |
| `{github_ref_name}` | `GITHUB_REF_NAME` | Branch or tag name |
| `{github_ref_type}` | `GITHUB_REF_TYPE` | `branch` or `tag` |
| `{github_sha}` | `GITHUB_SHA` | Commit SHA for the run |
| `{github_workflow}` | `GITHUB_WORKFLOW` | Workflow name (may contain spaces or `/`) |
| `{github_event_name}` | `GITHUB_EVENT_NAME` | e.g. `push`, `workflow_dispatch` |
| `{github_actor}` | `GITHUB_ACTOR` | User that triggered the run |
| `{github_run_id}` | `context.runId` | Unique across GitHub |
| `{github_run_number}` | `context.runNumber` | Unique **within one workflow file**, not the whole repository |
| `{github_run_attempt}` | `GITHUB_RUN_ATTEMPT` | `1` on the first try; increments on *Re-run jobs* |

Recommended GitHub templates (rerun-safe, shorter run number):

```json
"TargetPathTemplate": "builds/{github_repository_name}/{github_ref_name}/{github_run_number}-{github_run_attempt}",
"LatestSymlinkTemplate": "builds/{github_repository_name}/latest"
```

For [run 33663544790](https://github.com/nefarius/DsHidMini/actions/runs/33663544790) (`DsHidMini`, branch `master`, run number `30`, attempt `2`) that resolves to `builds/DsHidMini/master/30-2`. Attempt 2 does not overwrite attempt 1.

Run numbers are unique only within a single workflow. If two workflows in the same repository can produce the same number, use `{github_run_id}-{github_run_attempt}` instead for repository-wide uniqueness.

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

This repository ships a reusable composite action that does that work for you: it lists artifacts from the **current** workflow run, builds the compatible payload (including the `github_*` placeholders documented above), and POSTs it to your receiver. Store the webhook URL (including the secret GUID path) as `WEBHOOK_URL`. The calling job needs `actions: read` so the action can list artifacts, and you must **upload artifacts before** invoking it.

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
