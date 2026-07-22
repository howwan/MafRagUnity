// Polyfill: C# 9 'record' / 'init' 访问器所需的 IsExternalInit 类型。
// Unity Api Compatibility Level = .NET Framework 4.x（unity-4.8-api）不含此类型（.NET 5 才引入），
// 而本程序集内的 `record Chunk` 需要它。放到 RagSqliteStore 程序集内（record 所在程序集可见）。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
