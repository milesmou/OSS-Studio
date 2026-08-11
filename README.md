# OSS Studio

基于 [Aprillz.MewUI](https://github.com/aprillz/MewUI) 的轻量 OSS 桌面客户端 UI 原型。

当前版本实现：

- 直接保存和管理多个 Bucket 配置，无需账号分组。
- 支持添加、编辑、删除 Bucket；AccessKey 安全保存在 Windows 凭据管理器。
- 右键左侧 Bucket 可快速编辑或删除本地配置。
- 通过阿里云 OSS C# SDK V2 读取 Bucket 当前路径下的真实文件和目录。
- 支持文件与目录下载，以及带确认提示的文件和目录递归删除。
- 点击左侧 Bucket，在主工作区打开或激活对应标签页。
- 多标签页同时保留不同 Bucket 的对象浏览现场。
- 支持关闭标签页、搜索当前 Bucket 对象和恢复上次工作区。
- 使用柔和灰绿浅色主题，并以青、琥珀、蓝、紫、珊瑚色区分操作。
- 对象列表使用 MewUI `GridView`，为后续大数据量分页/虚拟化预留结构。

## 运行

```powershell
dotnet run
```

应用默认以空工作区启动，Bucket 配置保存在本地，AccessKey 保存至 Windows 凭据管理器。对象浏览、下载和删除已接入真实 OSS，上传和传输队列仍待继续完善。
