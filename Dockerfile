FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Aquarella/Aquarella/Aquarella.csproj Aquarella/Aquarella/
COPY Aquarella/tools/DatabaseMaintenance/DatabaseMaintenance.csproj Aquarella/tools/DatabaseMaintenance/
RUN dotnet restore Aquarella/tools/DatabaseMaintenance/DatabaseMaintenance.csproj
COPY Aquarella/Aquarella/ Aquarella/Aquarella/
COPY Aquarella/tools/DatabaseMaintenance/ Aquarella/tools/DatabaseMaintenance/
RUN dotnet publish Aquarella/Aquarella/Aquarella.csproj -c Release -o /out --no-restore /p:UseAppHost=false
RUN dotnet publish Aquarella/tools/DatabaseMaintenance/DatabaseMaintenance.csproj -c Release -o /maintenance --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production
COPY --from=build /out/ ./
COPY --from=build /maintenance/ /maintenance/
# Maintenance is an explicit alternate command; normal startup never runs it.
CMD ["dotnet", "/app/Aquarella.dll"]
