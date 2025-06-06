# <img src="assets/NSS-128x128.png" align="left" />AppVeyor Artifacts Receiver

[![Docker Image CI](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml/badge.svg)](https://github.com/nefarius/AppVeyorArtifactsReceiver/actions/workflows/docker-image.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%209-blue.svg)

Web service listening for deployment webhook calls from [AppVeyor](https://www.appveyor.com/) CI/CD.

## About

This little project spawns a webhook web server you can point an AppVeyor deployment at to mirror new artifacts to the
local file system.

## Features

- Mirroring build artifacts to custom infrastructure to circumvent the one month retention policy.
- The server initiates the download of the build job artifacts, so the deployment step finishes fast with success. With other deployment methods, network hiccups can often cause the deployment to fail and needs manual intervention or retries.
- A `latest` subdirectory symlink can be auto-generated to provide a fixed URL to the latest build artifacts.
- Executable metadata like Win32 version resource information is extracted and placed into a hidden `.MyApp.exe.json` file for e.g. auto-updaters to consume and check if newer builds are available.

## How to set up

- Log into AppVeyor and [create a new deployment](https://ci.appveyor.com/environments/new) with the `Webhook` provider
- Specify the URL to wherever you're hosting the service (e.g. `https://ci.example.org/webhooks/7b544703-bdd0-4420-9b96-18208076d4df`)
    - **Important:** use a new, auto-generated GUID here and keep it secret!
- Adjust the `Webhooks` section in `appsettings.Production.json` to fit your environment (don't forget to put your GUID
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

## 3rd party credits

- [FastEndpoints](https://github.com/FastEndpoints/FastEndpoints)
- [Nefarius.Utilities.AspNetCore](https://github.com/nefarius/Nefarius.Utilities.AspNetCore)
- [Polly](https://github.com/App-vNext/Polly)
- [PeNet](https://github.com/secana/PeNet)
- [Serilog](https://serilog.net/)
