using System.Security.Cryptography;
using System.Text;

namespace SeatFlow.Infrastructure.Utils;

/// <summary>
/// 文件内容哈希工具。
/// </summary>
public static class ContentHashHelper
{
    /// <summary>计算 JSON 字符串的 SHA256 哈希（十六进制）。</summary>
    public static string ComputeSha256 (string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(bytes);
    }
}
