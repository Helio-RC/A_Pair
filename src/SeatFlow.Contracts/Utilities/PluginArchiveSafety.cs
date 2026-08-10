using System.IO.Compression;

namespace SeatFlow.Contracts.Utilities;

/// <summary>
/// 插件包（.ap-plugin）ZIP 归档安全校验，供宿主（Application）与打包工具（Plugins.Sdk）共享。
/// 检查压缩炸弹（条目数/总大小/压缩比）与路径遍历（ZIP Slip）风险。
/// </summary>
public static class PluginArchiveSafety
{
    /// <summary>ZIP 条目数量上限。</summary>
    public const int MaxEntryCount = 10000;

    /// <summary>解压后总大小上限（字节），默认 500 MB。</summary>
    public const long MaxUncompressedSize = 500 * 1024 * 1024;

    /// <summary>最大压缩比率（解压后大小 / 压缩大小），超过此值视为 ZIP 炸弹。</summary>
    public const int MaxCompressionRatio = 100;

    /// <summary>
    /// 验证 ZIP 文件是否安全。
    /// </summary>
    /// <param name="archivePath">ZIP 文件路径。</param>
    /// <returns>验证失败时返回错误描述；通过时返回 <c>null</c>。</returns>
    public static string? Validate (string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries;
        if (entries.Count > MaxEntryCount)
            return $"ZIP 条目数 ({entries.Count}) 超过上限 ({MaxEntryCount})";

        long totalUncompressed = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
                continue;

            // ZIP Slip 防护：禁止路径遍历和绝对路径
            if (entry.FullName.Contains("..") || Path.IsPathRooted(entry.FullName))
                return $"条目 \"{entry.FullName}\" 包含非法路径（禁止 ../ 或绝对路径）";

            var compressed = entry.CompressedLength;
            var uncompressed = entry.Length;

            totalUncompressed += uncompressed;
            if (totalUncompressed > MaxUncompressedSize)
                return $"ZIP 解压后总大小 ({totalUncompressed / 1024 / 1024:N0} MB) 超过上限 ({MaxUncompressedSize / 1024 / 1024:N0} MB)";

            if (compressed > 0 && uncompressed > 0)
            {
                var ratio = uncompressed / (double)compressed;
                if (ratio > MaxCompressionRatio)
                    return $"条目 \"{entry.FullName}\" 压缩比 ({ratio:N0}:1) 超过上限 ({MaxCompressionRatio}:1)，疑似 ZIP 炸弹";
            }
        }
        return null;
    }

    /// <summary>
    /// 验证 ZIP 文件是否安全，失败时抛出 <see cref="InvalidDataException"/>。
    /// </summary>
    /// <param name="archivePath">ZIP 文件路径。</param>
    /// <exception cref="InvalidDataException">归档不安全时抛出。</exception>
    public static void EnsureSafe (string archivePath)
    {
        var error = Validate(archivePath);
        if (error != null)
            throw new InvalidDataException(error);
    }

    /// <summary>
    /// 校验路径段（插件包 ID、条目 path 等）是否为安全的单段目录名：
    /// 拒绝路径分隔符、绝对路径、<c>..</c> 与非法文件名字符。
    /// 纵深防御：manifest 中的路径值同样来自不可信包，防路径遍历写入/读取。
    /// </summary>
    /// <param name="value">待校验的路径段。</param>
    /// <returns>非法时返回错误描述；合法时返回 <c>null</c>。</returns>
    public static string? ValidateSafePathSegment (string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "路径段不能为空";

        if (value is "." or "..")
            return $"路径段 \"{value}\" 非法（禁止 . 与 ..）";

        if (value.Contains('/') || value.Contains('\\'))
            return $"路径段 \"{value}\" 不能包含路径分隔符";

        if (Path.IsPathRooted(value))
            return $"路径段 \"{value}\" 不能是绝对路径";

        var invalidChars = Path.GetInvalidFileNameChars();
        if (value.IndexOfAny(invalidChars) >= 0)
            return $"路径段 \"{value}\" 包含非法文件名字符";

        return null;
    }

    /// <summary>
    /// 校验相对路径（条目 manifest/scriptFile/assembly 等）：
    /// 允许目录分隔符，但拒绝 <c>..</c> 路径遍历、绝对路径与非法路径字符。
    /// </summary>
    /// <param name="value">待校验的相对路径。</param>
    /// <returns>非法时返回错误描述；合法时返回 <c>null</c>。</returns>
    public static string? ValidateSafeRelativePath (string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "路径不能为空";

        if (Path.IsPathRooted(value))
            return $"路径 \"{value}\" 不能是绝对路径";

        var invalidChars = Path.GetInvalidPathChars();
        if (value.IndexOfAny(invalidChars) >= 0)
            return $"路径 \"{value}\" 包含非法路径字符";

        // 拒绝 .. 段（路径遍历）：按 / 与 \ 拆分逐段检查
        foreach (var segment in value.Split(['/' , '\\'] , StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
                return $"路径 \"{value}\" 包含非法路径遍历段（..）";
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"路径 \"{value}\" 包含非法文件名字符";
        }

        return null;
    }
}
