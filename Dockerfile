FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY AdGroupUserCompare.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish ./
COPY docker-entrypoint.sh /usr/local/bin/ad-group-user-compare-entrypoint
RUN chmod +x /usr/local/bin/ad-group-user-compare-entrypoint
ENTRYPOINT ["ad-group-user-compare-entrypoint"]
CMD ["dotnet", "AdGroupUserCompare.dll"]
