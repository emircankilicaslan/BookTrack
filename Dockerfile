# Çok aşamalı derleme: SDK ile yayımla, yalnızca çalışma zamanı imajıyla çalıştır.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY LibraryApp/LibraryApp/LibraryApp.csproj LibraryApp/LibraryApp/
RUN dotnet restore LibraryApp/LibraryApp/LibraryApp.csproj
COPY LibraryApp/LibraryApp/ LibraryApp/LibraryApp/
RUN dotnet publish LibraryApp/LibraryApp/LibraryApp.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Kök olmayan kullanıcı ile çalışır (aspnet imajındaki yerleşik "app" kullanıcısı).
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "LibraryApp.dll"]
