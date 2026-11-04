# syntax=docker/dockerfile:1
# ---- Web (React) ----
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- API (.NET) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY server/ ./
RUN dotnet publish FourC.Cmms.Api/FourC.Cmms.Api.csproj -c Release -o /app --no-self-contained

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
# Attachments are written here when local file storage is used (docker compose); the app runs as a non-root user.
RUN mkdir -p /data/files && chown -R $APP_UID:$APP_UID /data
# Build identifier (the git commit) reported by /api/health, so a deployment can confirm what is running.
ARG VERSION=dev
ENV ASPNETCORE_HTTP_PORTS=8080 \
    App__Version=$VERSION \
    Storage__Path=/data/files \
    DOTNET_RUNNING_IN_CONTAINER=true
VOLUME ["/data"]
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FourC.Cmms.Api.dll"]
