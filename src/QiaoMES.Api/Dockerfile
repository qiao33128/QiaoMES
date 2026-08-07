# ---------- 构建阶段 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 复制解决方案与项目文件（利用缓存）
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
