#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 4840

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["UA-EdgeHEMS.csproj", "."]
RUN dotnet restore "./UA-EdgeHEMS.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "UA-EdgeHEMS.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UA-EdgeHEMS.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UA-EdgeHEMS.dll"]