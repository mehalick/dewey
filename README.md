# Dewey

Personal book reading tracker. .NET 10 / Blazor WASM PWA + AOT Lambda API on AWS.

## Layout

```
src/
  Dewey.Shared/   shared DTOs/contracts
  Dewey.Api/      ASP.NET minimal API, native AOT, Lambda entrypoint
  Dewey.Web/      Blazor WASM PWA
  Dewey.Infra/    AWS CDK app
tests/
  Dewey.Api.Tests/
  Dewey.Web.Tests/
.github/workflows/deploy.yml
```

## Build

```
dotnet build Dewey.slnx
dotnet test Dewey.slnx
```

## Run API locally

```
dotnet run --project src/Dewey.Api
# GET http://localhost:5000/api/health
```

## Deploy

CI handles deploys on push to `main` via GitHub Actions OIDC →
`secrets.AWS_DEPLOY_ROLE_ARN`. For a manual deploy:

```
dotnet publish src/Dewey.Api -c Release -r linux-arm64 --self-contained -o artifacts/api
dotnet publish src/Dewey.Web -c Release -o artifacts/web-publish
DEWEY_API_PUBLISH_DIR=$PWD/artifacts/api \
DEWEY_WEB_PUBLISH_DIR=$PWD/artifacts/web-publish/wwwroot \
cdk --app "dotnet run --project src/Dewey.Infra/Dewey.Infra.csproj" deploy
```

## Status

M1 — skeleton: solution, projects, empty CDK stack with Auth/Data/Api/Web/Observability constructs, hello-world `/api/health`, CI workflow.
