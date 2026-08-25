# OSS Studio Agent 规范

本文件适用于仓库根目录及其所有子目录。后续 Agent 在分析、修改、验证和提交本项目时必须遵循本规范。

## 项目概览

- 产品：OSS Studio，面向阿里云 OSS 的 Windows 桌面客户端。
- 技术栈：C#、.NET 10、Aprillz.MewUI 0.20.1、Direct2D、AlibabaCloud.OSS.V2。
- 默认平台：Windows x64。
- UI 风格：柔和灰绿浅色主题，避免纯白高亮和过度饱和；青色为主色，琥珀、蓝、紫、珊瑚色用于操作点缀。
- 管理模型：按资源桶管理，不按账号分组；同一窗口可通过多标签页浏览多个资源桶。

## 目录职责

- `Program.cs`：应用启动、MewUI 后端和主题配置。
- `Models/`：资源桶、对象、凭据和工作区状态模型。
- `Services/`：OSS 操作、凭据安全存储、工作区持久化。
- `UI/`：主窗口、模态浮层、按钮反馈及资源桶编辑界面。
- `Assets/`：应用 PNG/ICO 图标源文件。
- `run.cmd`：开发运行入口。
- `publish.cmd`：NativeAOT 单文件发布入口。
- `Release/`、`bin/`、`obj/`、`artifacts/`：生成内容，不作为源代码直接编辑。

## 开发与验证

涉及源码、项目配置、资源文件或构建与发布脚本的改动，必须在仓库根目录执行：

```powershell
dotnet build .\OSS-Studio.csproj --no-restore --output .\artifacts\verify
```

并且必须执行：

```powershell
.\publish.cmd
```

只有 `publish.cmd` 执行成功，才能将此类改动视为完成；若发布失败，必须修复失败原因或明确向用户报告阻塞，不得跳过发布验证。

如果本次仅修改文档、Agent 规范或其他不会影响程序构建与运行结果的文本文件，则无需执行 `dotnet build` 和 `publish.cmd`。一旦改动中包含任何代码、项目配置、资源文件或构建与发布脚本，仍须执行上述完整验证。

发布结果应位于 `Release/OSS-Studio.exe`，且 `Release` 目录只包含该自包含单文件。发布参数要求：

- `Release` 配置；
- `win-x64`；
- `PublishAot=true`；
- `PublishTrimmed=true`；
- `TrimMode=full`；
- `SelfContained=true`；
- 不生成调试符号。

提交结果前至少执行：

```powershell
git diff -w --check
```

使用 `git diff` 时始终添加 `-w`，忽略空白和换行差异。

## NativeAOT 与裁剪约束

- `AlibabaCloud.OSS.V2` 使用反射式 `XmlSerializer` 解析 OSS 响应；必须保留为 `TrimmerRootAssembly`，否则对象列表会报 `There is an error in XML document (0, 0)`。
- 工作区 JSON 必须使用 `WorkspaceJsonContext` 源生成，禁止退回依赖反射的 `JsonSerializer.Serialize<T>` 或 `Deserialize<T>` 重载。
- 应用图标使用嵌入资源。不要改回复制到输出目录，否则会破坏单文件发布要求。
- 新增依赖前检查其 NativeAOT 和完整裁剪兼容性；不能仅压制 IL2026、IL3050 等警告而忽略运行时行为。

## UI 实现规范

- 所有业务弹窗使用主窗口内的模态浮层，不创建新的顶层窗口。
- 所有按钮应有悬浮提示、悬浮反馈和按下反馈。
- 对象列表保持固定列宽、无竖向网格线；行之间仅使用浅色细分隔线。
- 对象行和行首选择框均可切换选中状态；选中后整行改变颜色。
- 对象选中状态以 `_checkedObjectKeys` 为唯一业务状态源，不要依赖 `GridView` 原生当前行选择状态。
- 表头全选框由程序同步半选状态时，必须通过 `_updatingHeaderCheckBoxes` 抑制 `CheckedChanged` 反向回调。
- 切换目录、前进、后退或刷新时清除全部对象选中和悬浮状态。
- 对象双击使用 `GridView.ItemDoubleClicked` 内置事件；选择框和操作按钮区域必须抑制该事件，不参与双击打开。
- 双击目录进入目录；双击支持的文本文件打开预览编辑浮层。
- 首页按钮返回资源桶配置中的预设 OSS 目录，不是无条件返回 Bucket 顶层。
- “上一级”不能越过预设 OSS 目录。
- 列表使用虚拟化单元格，绑定时必须重新关联当前对象 Key，不能将旧行视觉状态带到新目录。

## OSS 与数据安全

- AccessKeyId 和 AccessKeySecret 只能保存在 Windows 凭据管理器，禁止写入工作区 JSON、日志、截图、测试数据或仓库文件。
- 禁止输出、提交或硬编码真实凭据。
- 删除资源桶配置只删除本地配置和本地凭据，不删除 OSS 上的真实 Bucket。
- 删除 OSS 文件或目录必须保留确认流程；目录删除按对象前缀递归处理。
- 预设 OSS 路径为空时表示 Bucket 顶层；非空路径统一规范为以 `/` 结尾。
- 不要重新加入示例资源桶、测试资源桶或伪造对象数据。
- 网络异常应向用户展示可理解的信息，同时保留 OSS 错误码和关键错误内容。

## 修改原则

- 修改前先定位现有状态源和事件链，避免增加第二套互相竞争的状态。
- 保持改动聚焦，不顺带重构无关模块。
- 工作区可能包含用户尚未提交的修改；不得覆盖、回退或清理无关变更。
- 不直接编辑生成目录中的文件。
- 新增源文件时使用 `OSSStudio` 根命名空间，并保持可空引用类型检查通过。
- 不以隐藏警告替代问题修复。
- UI 行为修改除编译外，应检查目录切换、标签页切换、列表虚拟化及重复点击场景。

## Git 规范

- 除非用户明确要求，否则不要提交或推送。
- 用户所说的“提交 git”表示：直接使用工程本地仓库的 `git` 命令完成检查、暂存、提交，并自动推送到当前远程分支。
- “提交 git”流程不依赖 GitHub CLI、GitHub 插件或其他发布工具，不要求安装或登录 `gh`，也不创建 Pull Request。
- 默认命令流程为：`git status`、`git diff -w`、必要验证、`git add -A`、`git commit`、`git push origin <当前分支>`，推送后再次检查分支同步状态。
- 除非用户明确要求排除，否则“提交 git”包含当前任务产生的全部工程改动以及 `Release/OSS-Studio.exe`。
- 提交前必须执行凭据泄漏检查，确认没有 AccessKeyId、AccessKeySecret 或其他敏感信息进入暂存区。
- 提交前检查当前分支、远程地址和变更范围，只提交本任务相关文件。
- 禁止使用 `git reset --hard`、强制检出或其他会丢失用户修改的命令。
- 提交信息应简洁说明用户可见结果，例如 `fix: preserve OSS XML models for NativeAOT`。
