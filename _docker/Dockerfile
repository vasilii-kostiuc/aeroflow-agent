FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/AeroFlow.Agent -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# ffplay for real playback when the container has an audio device; the default
# compose service runs with AGENT_Agent__Simulate=true and does not need it.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["dotnet", "AeroFlow.Agent.dll"]
