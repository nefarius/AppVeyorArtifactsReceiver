# <img src="assets/NSS-128x128.png" align="left" />AppVeyor Artifacts Receiver

[![Docker Image CI](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%209-blue.svg)

Web service listening for deployment webhook calls from [AppVeyor](https://www.appveyor.com/) CI/CD.

## About

This little project spawns a webhook web server you can point an AppVeyor deployment at to mirror new artifacts to the
local file system.

<!-- 

Docker build:

docker build --push -t containinger/avar:dev .

-->

## Features

- Mirroring build artifacts to custom infrastructure to circumvent the one-month retention policy.
- The server initiates the download of the build job artifacts, so the deployment step finishes fast with success. With
  other deployment methods, network hiccups can often cause the deployment to fail and need manual intervention or
  retries.
- A `latest` subdirectory symlink can be auto-generated to provide a fixed URL to the latest build artifacts.
- Executable metadata like Win32 version resource information is extracted and placed into a hidden `.MyApp.exe.json`
  file for e.g., auto-updaters to consume and check if newer builds are available.

## How to set up

- Log into AppVeyor and [create a new deployment](https://ci.appveyor.com/environments/new) with the `Webhook` provider
- Specify the URL to wherever you're hosting the service (e.g.
  `https://ci.example.org/webhooks/7b544703-bdd0-4420-9b96-18208076d4df`)
    - **Important:** use a new, auto-generated GUID here and keep it secret!
- Adjust the `Webhooks` section in `appsettings.Production.json` to fit your environment (remember to put your GUID
  there as well)

Once the service is set up and running, it listens to webhook requests that contain the artifact URLs to download from.
To use this new deployment, adapt the `appveyor.yml` like so:

```yml
deploy:
  - provider: Environment
    name: BUILDBOT
    on:
      appveyor_repo_tag: true
```

### GitHub Actions Support

With some trickery, the same server can be fed from GitHub actions as well!

#### Single build artifact

WIP

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
          # Get artifacts for this run
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

## 3rd party credits

- [FastEndpoints](https://github.com/FastEndpoints/FastEndpoints)
- [Nefarius.Utilities.AspNetCore](https://github.com/nefarius/Nefarius.Utilities.AspNetCore)
- [Polly](https://github.com/App-vNext/Polly)
- [PeNet](https://github.com/secana/PeNet)
- [Serilog](https://serilog.net/)
- [Serilog.Enrichers.Sensitive](https://github.com/serilog-contrib/Serilog.Enrichers.Sensitive)
