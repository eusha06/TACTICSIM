FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/TacticSim.Core/TacticSim.Core.csproj src/TacticSim.Core/
COPY src/TacticSim.Web/TacticSim.Web.csproj src/TacticSim.Web/
RUN dotnet restore src/TacticSim.Web/TacticSim.Web.csproj
COPY src/ src/
ARG SOURCE_REVISION=unknown
RUN dotnet publish src/TacticSim.Web/TacticSim.Web.csproj --configuration Release \
    --no-restore --output /publish /p:UseAppHost=false /p:SourceRevisionId=${SOURCE_REVISION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TacticSim.Web.dll"]
