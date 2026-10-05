FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Aquarella/Aquarella/Aquarella.csproj Aquarella/Aquarella/
RUN dotnet restore Aquarella/Aquarella/Aquarella.csproj
COPY Aquarella/Aquarella/ Aquarella/Aquarella/
RUN dotnet publish Aquarella/Aquarella/Aquarella.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /out/ ./
ENTRYPOINT ["dotnet", "Aquarella.dll"]
