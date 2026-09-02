# <img src="assets/NSS-128x128.png" align="left" /> AppVeyor Artifacts Receiver

[![Docker Image CI](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%209-blue.svg)

A webhook service that mirrors build artifacts from [AppVeyor](https://www.appveyor.com/) and [GitHub Actions](https://docs.github.com/en/actions) onto your own disk.

## About

This project hosts a small webhook server. When a CI job finishes, it POSTs artifact URLs; the receiver downloads them into a directory tree you control.

It started as an AppVeyor webhook target so builds could outlive AppVeyor's retention window. GitHub Actions is now a first-class source as well: this repository ships a composite action that lists the current run's artifacts, sends a compatible payload, and forwards a token so private `archive_download_url` values can be fetched. Both providers share the same webhook URL, path templates, latest symlink, timestamps, and PE metadata.

Use one receiver for either CI, or both at once (give each provider its own webhook GUID if you want different path layouts).

## Features

- **Artifact mirroring** — Keep build outputs on your own infrastructure after the upstream CI expires them (AppVeyor retention, GitHub Actions artifact retention, or both).
- **Provider-aware completion** — AppVeyor webhooks have a short timeout, so the server returns `OK` immediately and downloads in the background. GitHub requests that send `X-GitHub-Token` wait until downloads finish so the short-lived token stays valid.
- **Latest symlink** — When configured, a symbolic link is created or updated at the path given by `LatestSymlinkTemplate`, pointing at the directory for the current build (derived from `TargetPathTemplate`). Use this for a stable URL to the newest artifacts.
- **Latest timestamp file** — When `TargetPathTemplate` is set, `LAST_UPDATED_AT.txt` is written in the build directory with the ISO 8601 timestamp when each deployment completes, for APIs or scripts to consume (independent of whether `LatestSymlinkTemplate` is configured).
- **SVG badge** — `LAST_UPDATED_AT.svg` is generated alongside the timestamp file under the same rules.
- **Executable metadata** — When `StoreMetaData` is enabled, Win32 version resource data (`FileVersion`, `ProductVersion`) is extracted from PE files (`.exe`, `.dll`, and similar) and written to hidden sidecar JSON next to the file (e.g. `.MyApp.exe.json`) for auto-updaters and other tools.
- **ZIP artifact metadata** — If the downloaded artifact is a ZIP, the same metadata extraction runs over entries inside the archive: entries are scanned up to a configurable limit, oversized entries are skipped, paths are validated (including zip-slip checks), and PEs without a typical extension are detected via the MZ header. Sidecars are stored under a hidden tree rooted at `.{sanitized_zip_basename}/`, mirroring the in-archive path (each directory segment is stored as a hidden segment; each file gets a `.filename.json` sidecar in the corresponding mirrored folder).

## Quick start

### Run with Docker

The image [Dockerfile](Dockerfile) exposes port **8080** by default for the base ASP.NET layer; in practice you configure the listen URL in your mounted `appsettings.Production.json` (the examples use **7089**). Map the host port to whatever port the app binds to inside the container.

```bash
docker build -t appveyor-artifacts-receiver .
docker run -d -p 7089:7089 \
  -v /path/to/data:/data \
  -v /path/to/appsettings.Production.json:/app/appsettings.Production.json:ro \
  appveyor-artifacts-receiver
```

See [docker-compose.example.yml](docker-compose.example.yml) for a full compose example.

### Configure a webhook

1. Generate a new GUID and keep it secret. The public URL is `https://ci.example.org/webhooks/<guid>`.
2. Copy [src/appsettings.Production.example.json](src/appsettings.Production.example.json) to `appsettings.Production.json`.
3. Tune `Kestrel` and `ServiceConfig:Webhooks`: use that same GUID as the dictionary key, set `RootDirectory`, and pick `TargetPathTemplate` / `LatestSymlinkTemplate` for the CI you will send. Timestamp and badge files are written whenever `TargetPathTemplate` is set; omit `LatestSymlinkTemplate` if you do not want a `latest` link.

Once running, the service accepts POSTs with artifact URLs to download. Wire up GitHub Actions, AppVeyor, or both.

## GitHub Actions

The bundled composite action lists artifacts from the **current** workflow run, builds the payload (including the `github_*` placeholders below), and POSTs it to your receiver.

Store the webhook URL (including the secret GUID path) as `WEBHOOK_URL`. The calling job needs `actions: read`, and you must **upload artifacts before** invoking the action. When artifact URLs are GitHub `archive_download_url` values, the action sends the workflow token in **`X-GitHub-Token`**. The receiver uses it as a Bearer token for the download and **waits until processing finishes** before responding with `OK`.

The `artifacts` array may contain **multiple** entries; each is downloaded in turn. Pin `uses:` to a tag or commit in production if you do not want to follow `master`.

Recommended path templates (rerun-safe):

```json
"TargetPathTemplate": "builds/{github_repository_name}/{github_ref_name}/{github_run_number}-{github_run_attempt}",
"LatestSymlinkTemplate": "builds/{github_repository_name}/latest"
```

For [run 33663544790](https://github.com/nefarius/DsHidMini/actions/runs/33663544790) (`DsHidMini`, branch `master`, run number `30`, attempt `2`) that resolves to `builds/DsHidMini/master/30-2`. Attempt 2 does not overwrite attempt 1. On an unmerged pull request, `{github_ref_name}` is `<pr_number>/merge` (an extra path segment). See [Path placeholders](#path-placeholders) for the full catalog and when to prefer `{github_run_id}`.

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

## AppVeyor

In AppVeyor, [create a deployment](https://ci.appveyor.com/environments/new) with the **Webhook** provider and point it at `https://ci.example.org/webhooks/<guid>` (the same secret GUID as in `ServiceConfig:Webhooks`).

AppVeyor sends its deployment environment variables in the payload, so the example templates use `appveyor_*` names:

```json
"TargetPathTemplate": "builds/{appveyor_project_name}/{appveyor_repo_branch}/{appveyor_build_version}",
"LatestSymlinkTemplate": "builds/{appveyor_project_name}/latest"
```

Then trigger that environment from `appveyor.yml`:

```yml
deploy:
  - provider: Environment
    name: BUILDBOT
    on:
      appveyor_repo_tag: true
```

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

**Path safety:** After expanding `{placeholder}` tokens, the receiver resolves `TargetPathTemplate` and `LatestSymlinkTemplate` under `RootDirectory` and **rejects** rooted paths or `..` traversal that would escape that root (`Path.Combine` is not trusted alone: a rooted second argument replaces the root). Valid relative paths that stay inside the root are kept.

Placeholder values are still **not sanitized**. `github_ref_name` is not a guaranteed single segment: unmerged pull requests use `<pr_number>/merge` (for example `42/merge`). Workflow names, `github_ref`, and `github_repository` can also contain `/`. Those extra segments still create directories under `RootDirectory`. Sanitize placeholder values at the source, or use a guaranteed single-segment token (`github_run_id`, `github_run_number`, `github_sha`) before expanding templates.

#### GitHub Actions

The bundled action always sends a native `github_*` catalog **and** three AppVeyor-compatible aliases so older templates keep working. New GitHub-only templates should use the native names.

| Placeholder | Source | Notes |
| ----------- | ------ | ----- |
| `{github_repository_owner}` | `context.repo.owner` | Owner or org login |
| `{github_repository_name}` | Repository name | Single path segment; prefer this over `{github_repository}` |
| `{github_repository}` | `GITHUB_REPOSITORY` | `owner/name` — contains `/` |
| `{github_ref}` | `GITHUB_REF` | e.g. `refs/heads/master` — contains `/` |
| `{github_ref_name}` | `GITHUB_REF_NAME` | Branch or tag name; unmerged PRs are `<pr_number>/merge` and contain `/` |
| `{github_ref_type}` | `GITHUB_REF_TYPE` | `branch` or `tag` |
| `{github_sha}` | `GITHUB_SHA` | Commit SHA for the run |
| `{github_workflow}` | `GITHUB_WORKFLOW` | Workflow name (may contain spaces or `/`) |
| `{github_event_name}` | `GITHUB_EVENT_NAME` | e.g. `push`, `workflow_dispatch` |
| `{github_actor}` | `GITHUB_ACTOR` | User that triggered the run |
| `{github_run_id}` | `context.runId` | Unique **within the repository**; does not change on re-run |
| `{github_run_number}` | `context.runNumber` | Unique **within one workflow file**, not the whole repository |
| `{github_run_attempt}` | `GITHUB_RUN_ATTEMPT` | `1` on the first try; increments on *Re-run jobs* |

Recommended GitHub templates (rerun-safe, shorter run number):

```json
"TargetPathTemplate": "builds/{github_repository_name}/{github_ref_name}/{github_run_number}-{github_run_attempt}",
"LatestSymlinkTemplate": "builds/{github_repository_name}/latest"
```

Run numbers are unique only within a single workflow. `{github_run_id}` is unique within the repository (not globally across GitHub) and does not change on re-run; use `{github_run_id}-{github_run_attempt}` when you need a rerun-safe directory that stays unique across workflows in that repo.

**Compatibility aliases** (not unique per GitHub run):

| Placeholder | Source |
| ----------- | ------ |
| `{appveyor_project_name}` | Repository name (`DsHidMini`) |
| `{appveyor_repo_branch}` | `GITHUB_REF_NAME` (branch or tag name) |
| `{appveyor_build_version}` | Same as `{appveyor_repo_branch}` — **not** a GitHub run number |

Using the AppVeyor example template against GitHub therefore stores artifacts under `builds/DsHidMini/master/master`. Every later push or rerun of `master` overwrites that directory and retargets `latest` at it.

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

## Third-Party Credits

- [Polly](https://github.com/App-vNext/Polly)
- [PeNet](https://github.com/secana/PeNet)
- [Serilog](https://serilog.net/)
- [FastEndpoints](https://github.com/FastEndpoints/FastEndpoints)
- [Serilog.Enrichers.Sensitive](https://github.com/serilog-contrib/Serilog.Enrichers.Sensitive)
- [Nefarius.Utilities.AspNetCore](https://github.com/nefarius/Nefarius.Utilities.AspNetCore)
