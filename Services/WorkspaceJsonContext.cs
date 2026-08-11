using System.Text.Json.Serialization;
using OSSStudio.Models;

namespace OSSStudio.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WorkspaceState))]
internal partial class WorkspaceJsonContext : JsonSerializerContext;
