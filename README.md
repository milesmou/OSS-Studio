# OSS Studio

OSS Studio 是一款面向阿里云 OSS 的 Windows 桌面客户端，使用 C#、.NET 10、[Aprillz.MewUI](https://github.com/aprillz/MewUI) 和 [AlibabaCloud.OSS.V2](https://www.nuget.org/packages/AlibabaCloud.OSS.V2) 构建。

应用按资源桶管理连接，不额外引入账号分组；一个窗口可以通过多个标签页同时浏览不同资源桶。

## 功能

### 资源桶与工作区

- 添加、编辑和删除本地资源桶配置。
- 支持公共云 Endpoint、自定义 Endpoint 和 CNAME。
- 支持预设 OSS 起始目录和请求者付费模式。
- 多标签页浏览资源桶，并恢复上次打开的工作区。
- 单实例运行：重复启动时激活已经打开的窗口。
- 左侧资源桶与书签区域按 1:1 布局。

> 删除资源桶只会删除本地配置和本地凭据，不会删除 OSS 上的真实 Bucket。

### 对象浏览与管理

- 浏览当前 OSS 目录中的文件和子目录。
- 前进、后退、返回上一级、返回预设首页和刷新。
- 搜索当前打开目录中的对象。
- 单选、批量选择和全选对象。
- 创建、下载、复制、移动、重命名和删除对象。
- 对象右键菜单提供下载、复制、移动、重命名；文件还可获取无签名参数的原始地址，目录可添加为书签。
- 复制和移动时使用按需加载的目录树选择目标位置，修改对象名称即可同时重命名。
- 目标目录树支持新建、重命名和递归删除目录。
- 删除文件或目录前显示确认提示。

### 书签

- 收藏当前 OSS 目录，点击书签快速打开。
- 显示目录名称及所属资源桶。
- 可为书签自定义显示名称；使用自定义名称后不再追加资源桶后缀。
- 删除或移动目录时同步清理或更新相关书签。

### 上传、下载与传输任务

- 通过按钮选择文件或目录上传。
- 从 Windows 资源管理器将文件或目录拖入对象列表上传。
- 下载单个文件、目录或批量对象。
- 上传和下载进度统一显示在“传输任务”弹窗中。
- 可取消正在执行的任务，并对失败任务发起重试。
- 本地存在重名文件时自动生成不冲突的名称；完成信息会显示文件总数和自动重命名数量。
- 可在设置中调整上传下载并发数、OSS 请求超时时间和自动重试次数。

默认设置：

| 设置 | 默认值 | 可选范围 |
| --- | ---: | --- |
| 上传下载并发数 | 3 | 1–8 |
| 请求超时时间 | 60 秒 | 10–300 秒 |
| 自动重试次数 | 5 次 | 0–5 次 |

### 文件预览与编辑

- 双击目录进入该目录。
- 双击文件打开统一预览窗口。
- 支持的文本类型可直接编辑，并以 UTF-8 编码保存回 OSS。
- PNG、JPEG、BMP 和 ICO 文件显示图片预览。
- 其他类型会提示无法预览，并允许尝试作为 UTF-8 文本打开。
- 预览窗口提供下载和获取原始对象地址操作。
- 文本在线编辑上限为 2 MB，图片预览上限为 20 MB。

> “获取地址”复制不带签名、过期时间等查询参数的原始 URL。私有对象仍需要相应访问权限。

### 界面与主题

- 支持浅色、深色和跟随 Windows 系统三种主题模式。
- 修改主题后可选择立即重启应用或稍后生效。
- 对象列表采用虚拟化单元格，适合浏览较多对象。
- 对象双击判定间隔为 250 ms。

## 安全与本地数据

- `AccessKeyId` 和 `AccessKeySecret` 仅保存在 Windows 凭据管理器。
- AccessKey 不会写入工作区 JSON、日志或项目文件。
- 资源桶、标签页、书签和应用设置保存在：

  ```text
  %LOCALAPPDATA%\OSS-Studio\workspace.json
  ```

- 请遵循最小权限原则配置 RAM 用户权限，并定期轮换 AccessKey。

## 系统要求

运行发布版本：

- Windows 10/11 x64。
- 无需预装 .NET Runtime，发布程序为自包含 NativeAOT 单文件。

从源码构建：

- Windows 10/11 x64。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
- Visual Studio Build Tools 或 Visual Studio，并安装适用于 NativeAOT 的 C++ 构建工具。

## 运行与开发

克隆仓库后，在项目根目录执行：

```powershell
dotnet restore
dotnet run
```

执行普通构建验证：

```powershell
dotnet build .\OSS-Studio.csproj --no-restore --output .\artifacts\verify
```

## 发布

执行：

```powershell
.\publish.cmd
```

发布脚本使用以下配置：

- `Release`
- `win-x64`
- `SelfContained=true`
- `PublishAot=true`
- `PublishTrimmed=true`
- `TrimMode=full`
- 不生成调试符号

发布完成后生成：

```text
Release\OSS-Studio.exe
```

`Release` 目录只包含这个自包含单文件。

> 如果正在运行 `Release\OSS-Studio.exe`，Windows 会锁定该文件。重新发布前请先关闭正在运行的发布版本。

## 项目结构

```text
Assets/      应用图标资源
Models/      资源桶、对象、书签和工作区状态模型
Services/    OSS 操作、凭据存储、剪贴板和工作区持久化
UI/          主窗口、设置、传输任务、预览及各类模态浮层
Program.cs   应用入口、单实例启动和主题初始化
publish.cmd  NativeAOT 单文件发布脚本
```

## 当前限制

- 复制和移动仅支持同一 Bucket 内操作。
- OSS 没有原生目录概念；目录通过对象前缀表示。
- 目录移动和重命名会先复制目录内对象，全部复制成功后再删除源对象。
- 搜索范围仅为当前打开目录中已经加载的对象，不会递归搜索整个 Bucket。
- 原始对象地址不包含签名参数，私有对象无法通过该地址匿名访问。

## 技术说明

- 目标框架：.NET 10。
- UI：Aprillz.MewUI 0.19.1，Direct2D 后端。
- OSS SDK：AlibabaCloud.OSS.V2 0.2.0。
- 发布方式：Windows x64 NativeAOT、自包含、完整裁剪、单文件。
- 工作区 JSON 使用源生成序列化上下文，适配 NativeAOT 和完整裁剪。
- `AlibabaCloud.OSS.V2` 作为裁剪根程序集保留，以确保 SDK 的 XML 响应模型可在 NativeAOT 环境中正常反序列化。
