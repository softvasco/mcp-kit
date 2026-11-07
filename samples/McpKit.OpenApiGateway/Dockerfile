# build from the repo root: docker build -f samples/McpKit.OpenApiGateway/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY samples/McpKit.OpenApiGateway/ samples/McpKit.OpenApiGateway/
RUN dotnet publish samples/McpKit.OpenApiGateway -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Upstream__Document=/config/openapi.json
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "McpKit.OpenApiGateway.dll"]
