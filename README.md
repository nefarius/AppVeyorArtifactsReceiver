# <img src="assets/NSS-128x128.png" align="left" /> AppVeyor Artifacts Receiver

[![Docker Image CI](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%209-blue.svg)

A web service that listens for deployment webhook calls from [AppVeyor](https://www.appveyor.com/) CI/CD and mirrors build artifacts to your local file system.

## About

This project hosts a webhook server that you can point an AppVeyor deployment to. When new builds complete, it automatically downloads and stores the artifacts locally, bypassing AppVeyor's retention limits.

## Features

- **Artifact mirroring** — Store build artifacts on your own infrastructure to circumvent the one-month retention policy.
- **Fast deployment completion** — The server initiates artifact downloads asynchronously, so the deployment step finishes quickly and successfully. With other methods, network hiccups often cause deployments to fail and require manual retries.
- **Latest symlink** — A `latest` subdirectory symlink can be auto-generated to provide a fixed URL for the most recent build artifacts.
- **Latest timestamp file** — `LAST_UPDATED_AT.txt` is written with the ISO 8601 timestamp when each deployment completes, for APIs or scripts to consume.
- **SVG badge** — `LAST_UPDATED_AT.svg` is generated as a human-readable badge showing the last update time, suitable for embedding in web pages or README files.
- **Executable metadata** — Win32 version resource information is extracted from executables and written to hidden `.{filename}.json` files (e.g. `.MyApp.exe.json`) for auto-updaters and other tools to consume.

## Quick Start

### Running with Docker

```bash
docker build -t appveyor-artifacts-receiver .
docker run -d -p 7089:7089 \
  -v /path/to/data:/data \
  -v /path/to/appsettings.Production.json:/app/appsettings.Production.json:ro \
  appveyor-artifacts-receiver
```

Use the port from your `appsettings.Production.json` (default: 7089). See [docker-compose.example.yml](docker-compose.example.yml) for a full example.

### Configuration

1. Log into AppVeyor and [create a new deployment](https://ci.appveyor.com/environments/new) with the **Webhook** provider.
2. Specify the URL where you host the service (e.g. `https://ci.example.org/webhooks/7b544703-bdd0-4420-9b96-18208076d4df`).
   - **Important:** Use a new, auto-generated GUID and keep it secret.
3. Adjust the `Webhooks` section in `appsettings.Production.json` to match your environment (include your GUID there as well).

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

The same server can receive webhooks from GitHub Actions with a compatible payload.

### Single Build Artifact

Example GitHub Actions job (supports one artifact per run):

```yml
name: Build and Upload to Buildbot

on:
  push:
    tags:
      - "v*"

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

      - name: Get artifact metadata
        id: get_artifact
        shell: bash
        run: |
          response=$(curl -s -H "Authorization: Bearer ${{ secrets.GITHUB_TOKEN }}" \
            https://api.github.com/repos/${{ github.repository }}/actions/runs/${{ github.run_id }}/artifacts)

          artifact_id=$(echo "$response" | jq -r '.artifacts[0].id')
          artifact_name=$(echo "$response" | jq -r '.artifacts[0].name')
          artifact_url=$(echo "$response" | jq -r ".artifacts[] | select(.id==$artifact_id) | .archive_download_url")

          echo "file=$artifact_name.zip" >> $GITHUB_OUTPUT
          echo "url=$artifact_url" >> $GITHUB_OUTPUT

      - name: Send webhook
        shell: bash
        run: |
          payload=$(jq -n \
            --arg fileName "${{ steps.get_artifact.outputs.file }}" \
            --arg url "${{ steps.get_artifact.outputs.url }}" \
            --arg projectName "${{ github.event.repository.name }}" \
            --arg branch "${{ github.ref_name }}" \
            --arg buildVersion "${{ github.ref_name }}" \
            '{
              artifacts: [
                {
                  fileName: $fileName,
                  url: $url
                }
              ],
              environmentVariables: {
                appveyor_project_name: $projectName,
                appveyor_repo_branch: $branch,
                appveyor_build_version: $buildVersion
              }
            }')

          curl -X POST "${{ secrets.WEBHOOK_URL }}" \
            -H "Content-Type: application/json" \
            -H "X-GitHub-Token: ${{ secrets.GITHUB_TOKEN }}" \
            -d "$payload"
```

## Third-Party Credits

- [Polly](https://github.com/App-vNext/Polly)
- [PeNet](https://github.com/secana/PeNet)
- [Serilog](https://serilog.net/)
- [FastEndpoints](https://github.com/FastEndpoints/FastEndpoints)
- [Serilog.Enrichers.Sensitive](https://github.com/serilog-contrib/Serilog.Enrichers.Sensitive)
- [Nefarius.Utilities.AspNetCore](https://github.com/nefarius/Nefarius.Utilities.AspNetCore)
