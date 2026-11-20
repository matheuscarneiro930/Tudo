# Servidor do Fodinha Online (usa o mesmo núcleo de regras do app).
# Build:  docker build -t fodinha-server .
# Rodar:  docker run -p 8080:8080 fodinha-server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Assets/Fodinha/Scripts/Core Assets/Fodinha/Scripts/Core
COPY Assets/Fodinha/Scripts/Hosting Assets/Fodinha/Scripts/Hosting
COPY Assets/Fodinha/Scripts/Net Assets/Fodinha/Scripts/Net
COPY Tools/CoreBuild Tools/CoreBuild
COPY Tools/HostingBuild Tools/HostingBuild
COPY Tools/NetBuild Tools/NetBuild
COPY Server/Fodinha.Server Server/Fodinha.Server
RUN dotnet publish Server/Fodinha.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fodinha.Server.dll"]
