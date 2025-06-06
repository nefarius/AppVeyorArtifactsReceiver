#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/AppVeyorArtifactsReceiver.csproj", "."]
RUN dotnet restore "./AppVeyorArtifactsReceiver.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "AppVeyorArtifactsReceiver.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AppVeyorArtifactsReceiver.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AppVeyorArtifactsReceiver.dll"]