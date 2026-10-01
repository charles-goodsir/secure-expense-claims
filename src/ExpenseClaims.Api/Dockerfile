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
USER $APP_UID
ENTRYPOINT ["dotnet", "ExpenseClaims.Api.dll"]
