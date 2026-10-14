FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY nuget.config Directory.Build.props global.json ./
COPY src/ ./src/
RUN dotnet restore src/PaymentIntegration.Api/PaymentIntegration.Api.csproj
RUN dotnet publish src/PaymentIntegration.Api/PaymentIntegration.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PaymentIntegration.Api.dll"]
