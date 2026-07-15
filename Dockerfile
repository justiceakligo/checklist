FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AtlasChecklist.slnx ./
COPY src/Atlas.Domain/Atlas.Domain.csproj src/Atlas.Domain/
COPY src/Atlas.Application/Atlas.Application.csproj src/Atlas.Application/
COPY src/Atlas.Infrastructure/Atlas.Infrastructure.csproj src/Atlas.Infrastructure/
COPY src/Atlas.Api/Atlas.Api.csproj src/Atlas.Api/
RUN dotnet restore src/Atlas.Api/Atlas.Api.csproj

COPY . .
RUN dotnet publish src/Atlas.Api/Atlas.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Atlas.Api.dll"]
