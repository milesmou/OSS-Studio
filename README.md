# OSS Studio

OSS Studio 是一款面向阿里云 OSS 的 Windows 桌面客户端，当前唯一实现基于 Tauri 2、Rust、React 19、TypeScript 和 Vite。

> 早期 .NET / MewUI 版本已从仓库移除。`release/OSS-Studio-Old.exe` 是保留的最后一个旧版单文件，仅用于回退，不再维护，也不提供源码构建。

应用按资源桶管理连接，不额外引入账号分组；同一窗口可通过多个标签页浏览不同资源桶。

## 功能

### 资源桶与工作区

- 添加、编辑和删除本地资源桶配置，支持自定义 Endpoint、CNAME 和预设 OSS 起始目录。
- 多标签页浏览资源桶，并恢复上次打开的工作区。
- 资源桶本地显示名称、排序、书签和当前目录收藏。
- 单实例运行，重复启动会激活已经打开的窗口。

### 对象浏览与管理

- 浏览、前进、后退、返回预设目录、刷新和搜索当前目录。
- 单选、批量选择、全选，以及上传、下载、复制、移动、重命名和递归删除。
- 拖放文件或目录上传，统一传输任务面板支持进度、取消和失败重试。
- 文本文件在线预览与 UTF-8 保存；图片预览与缩放。
- 复制和移动使用目录树选择目标位置，修改对象名称即可同时重命名。

### 界面

- 浅色、深色和跟随 Windows 系统三种主题。
- 深海青配色与独立品牌图标。
- 对象列表虚拟化，支持大量对象浏览。
- 禁用 WebView 浏览器右键菜单；资源桶、对象和书签使用应用内原生菜单。

## 安全与本地数据

- `AccessKeyId` 和 `AccessKeySecret` 仅保存在 Windows 凭据管理器。
- 工作区 JSON 不包含 AccessKey。
- 资源桶、标签页、书签和应用设置保存在：

  ```text
  %LOCALAPPDATA%\OSS-Studio\workspace.json
  ```

- 删除资源桶只删除本地配置和本地凭据，不删除 OSS 上的真实 Bucket。
- 删除 OSS 文件或目录前必须经过确认。

## 系统要求

- Windows 10/11 x64。
- Microsoft Edge WebView2 Runtime（Windows 11 通常已安装）。
- 开发环境需要 Node.js 20.19+ 或 22.12+，以及 Rust stable MSVC 工具链。

## 开发

在根目录直接启动调试运行：

```powershell
.\run.cmd
```

也可以手动进入 Tauri 目录：

```powershell
cd .\tauri
npm install
npm run tauri -- dev
```

前端构建：

```powershell
npm run build
```

Rust 测试：

```powershell
cd .\src-tauri
cargo test
```

## 发布

```powershell
.\publish.cmd
```

当前版本发布结果：

```text
release\OSS-Studio.exe
```

旧版回退文件：

```text
release\OSS-Studio-Old.exe
```

## 目录

```text
tauri/                 当前唯一实现
tauri/src/             React 界面与交互状态
tauri/src-tauri/src/   Tauri 命令、OSS 客户端、工作区与凭据存储
tauri/src-tauri/icons/ 品牌图标与 Windows 图标资源
release/               当前发布程序与旧版回退文件
```

## 当前限制

- 复制和移动仅支持同一 Bucket。
- 搜索范围仅为当前目录中已经加载的对象。
- OSS 没有原生目录，目录由对象前缀表示。
- 单个对象使用普通 PUT 上传，超大文件建议在 OSS 控制台或专门工具中处理。
- 图片预览通过 IPC 传输 Base64 数据，大图片预览会产生一次性内存开销。
