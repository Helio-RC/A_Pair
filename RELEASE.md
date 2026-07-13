# SeatFlow v1.4.0 发布说明

本次发布完成了从便携式单文件到标准安装包的分发模式迁移，数据存储迁移到操作系统标准用户数据目录。

## 重大变更
- 取消单文件发布（PublishSingleFile），改为标准 dotnet publish 文件夹输出
- Velopack 安装包作为主要分发形式（自动更新），同时保留 zip/tar.gz 便携包
- 数据存储从 `{exeDir}/AppData/` 迁移到 OS 标准路径（Windows: `%APPDATA%\SeatFlow\`，Linux: `~/.local/share/SeatFlow/`，macOS: `~/Library/Application Support/SeatFlow/`）
- 安装时自动复制安装程序同目录下的 .seatsets 文件到应用目录

## 改进
- 移除启动时的目录清洁检查（CheckCleanDirectory），适配安装包目录结构
- 插件目录支持 Velopack 安装模式（RootAppDir/Plugins）

## 修复
- 数据目录不再依赖 exe 所在位置，避免更新导致数据丢失
