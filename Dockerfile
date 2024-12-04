# syntax=docker/dockerfile:1
# Learn about building .NET container images:
# https://github.com/dotnet/dotnet-docker/blob/main/samples/README.md
FROM mcr.microsoft.com/dotnet/nightly/sdk:8.0-alpine-aot AS build
ARG TARGETARCH
WORKDIR /source

# Copy project file and restore as distinct layers
COPY --link NuGet.config .
COPY --link *.csproj .
COPY --link *.config .
RUN dotnet restore -r linux-musl-$TARGETARCH o10y-dotnet.linux.csproj

# Copy source code and publish app
COPY --link . .
RUN dotnet publish --no-restore -o /app o10y-dotnet.linux.csproj
RUN rm -f /app/*.dbg /app/*.Development.json


# Final stage/image
FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:8.0-alpine-aot

WORKDIR /app
COPY --link --from=build /app .
USER $APP_UID
ENTRYPOINT ["./o10y-dotnet.linux"]
