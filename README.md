# OSS Studio

基于 [Aprillz.MewUI](https://github.com/aprillz/MewUI) 的轻量 OSS 桌面客户端 UI 原型。

当前版本实现：

- 单账号记录多个 Bucket，并可在账号间切换。
- 点击左侧 Bucket，在主工作区打开或激活对应标签页。
- 多标签页同时保留不同 Bucket 的对象浏览现场。
- 支持关闭标签页、搜索当前 Bucket 对象和恢复上次工作区。
- 使用柔和灰绿浅色主题，并以青、琥珀、蓝、紫、珊瑚色区分操作。
- 对象列表使用 MewUI `GridView`，为后续大数据量分页/虚拟化预留结构。

## 运行

```powershell
dotnet run
```

当前对象与账号数据为本地演示数据。下一阶段可接入阿里云 OSS SDK、系统凭据存储与真实传输队列。
