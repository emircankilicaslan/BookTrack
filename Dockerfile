# Çok aşamalı derleme: SDK ile yayımla, yalnızca çalışma zamanı imajıyla çalıştır.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY LibraryApp/LibraryApp/LibraryApp.csproj LibraryApp/LibraryApp/
RUN dotnet restore LibraryApp/LibraryApp/LibraryApp.csproj
COPY LibraryApp/LibraryApp/ LibraryApp/LibraryApp/
RUN dotnet publish LibraryApp/LibraryApp/LibraryApp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Kök olmayan kullanıcı ile çalışır (aspnet imajındaki yerleşik "app" kullanıcısı).
USER app
EXPOSE 8080
# aspnet imajında curl yoktur; bash ile /health/live çağrılır.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.0\r\n\r\n' >&3 && head -n1 <&3 | grep -q 200"]
ENTRYPOINT ["dotnet", "LibraryApp.dll"]
