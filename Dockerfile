FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["TransportesGutierrez.Api.csproj", "./"]
RUN dotnet restore "TransportesGutierrez.Api.csproj"

COPY . .
RUN dotnet publish "TransportesGutierrez.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 10000
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} dotnet TransportesGutierrez.Api.dll"]
