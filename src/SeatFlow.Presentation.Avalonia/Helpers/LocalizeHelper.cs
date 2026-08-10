using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SeatFlow.Presentation.Avalonia.Helpers;

/// <summary>
/// 多语言字典解析工具。将 manifest 中声明的 <c>Dictionary&lt;string, string&gt;</c>
/// 按当前 UI 文化解析为单个显示字符串。内置策略和插件统一使用此机制。
/// </summary>
public static class LocalizeHelper
{
    /// <summary>
    /// 根据 <see cref="CultureInfo.CurrentUICulture"/> 从多语言字典中选取最佳匹配文本。
    /// 回退顺序：当前语言 → zh-CN → 字典第一个值 → 空字符串。
    /// </summary>
    public static string Resolve (Dictionary<string , string> dict)
    {
        if (dict.Count == 0)
            return string.Empty;

        var culture = CultureInfo.CurrentUICulture.Name;
        if (dict.TryGetValue(culture , out var value))
            return value;

        // 尝试仅匹配语言前缀（如 "zh" 匹配 "zh-CN"）
        if (culture.Contains('-'))
        {
            var prefix = culture[..culture.IndexOf('-')];
            foreach (var kv in dict)
            {
                if (kv.Key.StartsWith(prefix , StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
        }

        if (dict.TryGetValue("zh-CN" , out var fallback))
            return fallback;

        return dict.Values.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// 安全解析可能为 null 的字典。
    /// </summary>
    public static string SafeResolve (Dictionary<string , string>? dict)
        => dict is not null ? Resolve(dict) : string.Empty;
}
