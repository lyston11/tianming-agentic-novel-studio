FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5002

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY tianming-agentic-novel-studio/ tianming-agentic-novel-studio/
COPY tianming-novel-ai-writer/Framework/Common/Helpers/ tianming-novel-ai-writer/Framework/Common/Helpers/
WORKDIR "/src/tianming-agentic-novel-studio/Web/NovelAgentWeb"
RUN dotnet restore
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p App_Data/Database App_Data/Projects
ENTRYPOINT ["dotnet", "NovelAgentWeb.dll"]
