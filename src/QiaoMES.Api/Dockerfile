# ---------- 构建阶段 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 只复制 .slnx 与全部 .csproj（利用 Docker 层缓存：只要项目文件没变，restore 这层就能复用）
# ⚠️ 新增模块时，这里要同步补一行，否则 restore 层会失效（构建变慢，但仍能通过 —— dotnet restore
#    对缺失的 ProjectReference 是容忍的，不要因此以为"不用加"）。
COPY src/QiaoMES.slnx .
COPY src/QiaoMES.Api/*.csproj ./QiaoMES.Api/
COPY src/BuildingBlocks/Shared/QiaoMES.Shared.csproj ./BuildingBlocks/Shared/
COPY src/BuildingBlocks/Infrastructure/QiaoMES.Infrastructure/QiaoMES.Infrastructure.csproj ./BuildingBlocks/Infrastructure/QiaoMES.Infrastructure/
COPY src/Modules/Identity/Domain/QiaoMES.Identity.Domain.csproj ./Modules/Identity/Domain/
COPY src/Modules/Identity/Application/QiaoMES.Identity.Application.csproj ./Modules/Identity/Application/
COPY src/Modules/Identity/Infrastructure/QiaoMES.Identity.Infrastructure.csproj ./Modules/Identity/Infrastructure/
COPY src/Modules/Identity/Api/QiaoMES.Identity.Api.csproj ./Modules/Identity/Api/
COPY src/Modules/Production/Domain/QiaoMES.Production.Domain.csproj ./Modules/Production/Domain/
COPY src/Modules/Production/Application/QiaoMES.Production.Application.csproj ./Modules/Production/Application/
COPY src/Modules/Production/Infrastructure/QiaoMES.Production.Infrastructure.csproj ./Modules/Production/Infrastructure/
COPY src/Modules/Production/Api/QiaoMES.Production.Api.csproj ./Modules/Production/Api/
COPY src/Modules/MasterData/Domain/QiaoMES.MasterData.Domain.csproj ./Modules/MasterData/Domain/
COPY src/Modules/MasterData/Application/QiaoMES.MasterData.Application.csproj ./Modules/MasterData/Application/
COPY src/Modules/MasterData/Infrastructure/QiaoMES.MasterData.Infrastructure.csproj ./Modules/MasterData/Infrastructure/
COPY src/Modules/MasterData/Api/QiaoMES.MasterData.Api.csproj ./Modules/MasterData/Api/
COPY src/Modules/Quality/Domain/QiaoMES.Quality.Domain.csproj ./Modules/Quality/Domain/
COPY src/Modules/Quality/Application/QiaoMES.Quality.Application.csproj ./Modules/Quality/Application/
COPY src/Modules/Quality/Infrastructure/QiaoMES.Quality.Infrastructure.csproj ./Modules/Quality/Infrastructure/
COPY src/Modules/Quality/Api/QiaoMES.Quality.Api.csproj ./Modules/Quality/Api/
COPY src/Modules/Equipment/Domain/QiaoMES.Equipment.Domain.csproj ./Modules/Equipment/Domain/
COPY src/Modules/Equipment/Application/QiaoMES.Equipment.Application.csproj ./Modules/Equipment/Application/
COPY src/Modules/Equipment/Infrastructure/QiaoMES.Equipment.Infrastructure.csproj ./Modules/Equipment/Infrastructure/
COPY src/Modules/Equipment/Api/QiaoMES.Equipment.Api.csproj ./Modules/Equipment/Api/
COPY src/Modules/Reporting/Domain/QiaoMES.Reporting.Domain.csproj ./Modules/Reporting/Domain/
COPY src/Modules/Reporting/Application/QiaoMES.Reporting.Application.csproj ./Modules/Reporting/Application/
COPY src/Modules/Reporting/Infrastructure/QiaoMES.Reporting.Infrastructure.csproj ./Modules/Reporting/Infrastructure/
COPY src/Modules/Reporting/Api/QiaoMES.Reporting.Api.csproj ./Modules/Reporting/Api/
COPY src/Modules/Assistant/Domain/QiaoMES.Assistant.Domain.csproj ./Modules/Assistant/Domain/
COPY src/Modules/Assistant/Application/QiaoMES.Assistant.Application.csproj ./Modules/Assistant/Application/
COPY src/Modules/Assistant/Infrastructure/QiaoMES.Assistant.Infrastructure.csproj ./Modules/Assistant/Infrastructure/
COPY src/Modules/Assistant/Api/QiaoMES.Assistant.Api.csproj ./Modules/Assistant/Api/

# 清除宿主注入的 NuGet fallback 路径，避免 Windows 路径在 Linux 容器中报错
ENV NUGET_FALLBACK_PACKAGES=""

# 还原依赖（csproj 已复制到 /src/QiaoMES.Api 等相对位置）
RUN dotnet restore QiaoMES.Api/QiaoMES.Api.csproj

# 复制全部源码到工作目录并发布
COPY src/ ./
WORKDIR /src/QiaoMES.Api
RUN dotnet publish QiaoMES.Api.csproj -c Release -o /app/publish

# ---------- 运行阶段 ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 以非 root 用户运行（镜像内置 app 用户，UID 1000）
USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "QiaoMES.Api.dll"]
