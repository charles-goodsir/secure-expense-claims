# The web app, built with Entra settings baked in (VITE_ values are public, not secrets).
FROM node:24-alpine AS web
ARG VITE_ENTRA_CLIENT_ID
ARG VITE_ENTRA_TENANT_ID
ARG VITE_API_SCOPE
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/ExpenseClaims.Api/ExpenseClaims.Api.csproj src/ExpenseClaims.Api/packages.lock.json src/ExpenseClaims.Api/
RUN dotnet restore src/ExpenseClaims.Api --locked-mode
COPY src/ExpenseClaims.Api/ src/ExpenseClaims.Api/
RUN dotnet publish src/ExpenseClaims.Api --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
# The API serves the web app from the same origin, so there's no CORS to configure.
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpenseClaims.Api.dll"]
