# OSS Studio Agent 规范

本文件适用于仓库根目录及其所有子目录。后续 Agent 在分析、修改、验证和提交本项目时必须遵循本规范。

## 项目结构

- 当前唯一实现位于 `tauri/`，技术栈为 Tauri 2、Rust、React 19、TypeScript、Vite。
- 早期 .NET / MewUI 源码、项目文件和构建脚本已删除；不要重新引入 .NET 实现或 `.csproj`。
- `release/OSS-Studio-Old.exe` 是保留的旧版单文件回退产物，禁止删除、覆盖或改名为当前版本。
- 当前版本发布产物位于 `release\OSS-Studio.exe`。
- 根目录 `run.cmd` 用于调试运行，`publish.cmd` 用于发布并复制到 `release\`。
- `tauri\node_modules\`、`tauri\dist\` 和 `tauri\src-tauri\target\` 为生成内容，不直接编辑。
- `release\OSS-Studio.exe` 为当前发布产物，`release\OSS-Studio-Old.exe` 为旧版回退产物。

## 开发与验证

前端和 Rust 代码改动必须在 `tauri/` 目录执行：

```powershell
npm run build
```

```powershell
cd .\src-tauri
cargo fmt -- --check
cargo test
```

涉及可执行文件、Tauri 配置、Rust 代码或发布脚本的改动，还必须执行：

```powershell
.\publish.cmd
```

发布结果必须位于 `release\OSS-Studio.exe`。该程序依赖 Microsoft Edge WebView2 Runtime，不是 .NET NativeAOT 单文件。

提交前执行：

```powershell
git diff -w --check
```

使用 `git diff` 时始终添加 `-w`，忽略空白和换行差异。

## OSS 与网络

- AccessKeyId 和 AccessKeySecret 只能写入 Windows 凭据管理器，禁止写入工作区 JSON、日志、截图、测试数据或仓库文件。
- 禁止输出、提交或硬编码真实凭据。
- 删除资源桶配置只删除本地配置与凭据，不删除 OSS Bucket。
- 删除 OSS 对象前必须保留用户确认流程；目录删除按对象前缀递归处理。
- 预设 OSS 路径为空表示 Bucket 顶层；非空路径统一规范为以 `/` 结尾。
- OSS 请求必须保留 Windows 系统代理支持，不要移除 `reqwest` 的 `system-proxy` 特性。
- 网络异常提示不得包含完整请求 URL、查询参数、签名或凭据；应保留端点主机、错误码和可读原因。
- 网络错误应支持自动重试，并允许用户通过刷新重新发起请求。

## UI 实现

- 业务弹窗使用应用内模态浮层，不创建额外顶层窗口。
- 资源桶、对象和书签的右键菜单使用 React 应用内菜单；保持 WebView 浏览器右键菜单禁用。
- 对象选中状态必须由 React 状态统一维护，不依赖 DOM 原生选择状态。
- 切换目录、前进、后退或刷新时清除对象选中和悬浮状态。
- 双击目录进入目录；双击支持的文本文件打开预览编辑浮层。
- 支持浅色、深色和跟随系统主题，新增颜色优先使用 `src/styles.css` 中的主题变量。
- 按钮应有悬浮提示、悬浮反馈和按下反馈。

## 修改原则

- 修改前先定位现有状态源和事件链，避免增加第二套互相竞争的状态。
- 保持改动聚焦，不顺带重构无关模块。
- 工作区可能包含用户尚未提交的修改；不得覆盖、回退或清理无关变更。
- 不以隐藏警告替代问题修复。
- UI 行为修改除编译外，应检查目录切换、标签页切换、列表虚拟化和重复点击场景。

## Git 规范

- 除非用户明确要求，否则不要提交或推送。
- 用户所说的“提交 git”表示：使用工程本地仓库的 `git` 命令完成检查、暂存、提交，并自动推送到当前远程分支。
- 默认流程为：`git status`、`git diff -w`、必要验证、`git add -A`、`git commit`、`git push origin <当前分支>`，推送后再次检查分支同步状态。
- 除非用户明确要求排除，“提交 git”包含当前 `release\OSS-Studio.exe` 与旧版 `release\OSS-Studio-Old.exe`。
- 提交前必须执行凭据泄漏检查。
- 禁止使用 `git reset --hard`、强制检出或其他会丢失用户修改的命令。
- 提交信息应简洁说明用户可见结果。
