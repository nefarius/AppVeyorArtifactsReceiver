# AppVeyor Artifacts Receiver

Web service listening for deployment webhook calls from AppVeyor CI/CD.

## How to set up

- Log into AppVeyor and [create a new deployment](https://ci.appveyor.com/environments/new) with the `Webhook` provider
- Specify the URL to wherever you're hosting the service (e.G. `https://ci.example.org/webhooks/7b544703-bdd0-4420-9b96-18208076d4df`)
  - **Important:** use a new, auto-generated GUID here and keep it secret!
- Adjust the `Webhooks` section in `appsettings.json` to fit your environment (don't forget to put your GUID there as well)

Once the service is set up and running, it listens to webhook requests that contain the artifact URLs to download from. To use this new deployment, adapt the `appveyor.yml` like so:

```yml
deploy:
- provider: Environment
  name: BUILDBOT
  on:
    appveyor_repo_tag: true
```

## 3rd party credits

- [FastEndpoints](https://github.com/dj-nitehawk/FastEndpoints)
