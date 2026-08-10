using System.Diagnostics;
using System.Reflection;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Plugins.Sdk.Attributes;

namespace SeatFlow.Plugins.Sdk.Abstractions;

/// <summary>
/// 所有插件的可选抽象基类，提供 <see cref="IPlugin"/> 身份元数据的默认实现。
/// 自动从 <see cref="PluginAttribute"/> 读取元数据，若无特性则使用回退值。
/// </summary>
/// <remarks>
/// <para>自动行为：</para>
/// <list type="bullet">
///   <item><see cref="Id"/> — 来自 <see cref="PluginAttribute.Id"/>，否则为随机 GUID</item>
///   <item><see cref="Name"/> — 来自 <see cref="PluginAttribute.Name"/>，否则为类型名</item>
///   <item><see cref="Version"/> — 来自 <see cref="PluginAttribute.Version"/>，否则为 "1.0.0"</item>
///   <item><see cref="Category"/> — 来自 <see cref="PluginAttribute.Category"/>，否则为 "strategy"</item>
/// </list>
/// <para>未来扩展新插件类型时，从此类派生子类添加行为接口即可。</para>
/// </remarks>
public abstract class PluginBase : IPlugin
{
    private readonly string _id;
    private readonly string _name;
    private readonly string _version;
    private readonly string _category;

    /// <summary>
    /// 由构造函数设置的 <see cref="PluginAttribute"/>，供子类复用，避免重复反射。
    /// </summary>
    private protected PluginAttribute? ResolvedAttribute { get; }

    /// <summary>
    /// 初始化基类，通过反射读取 <see cref="PluginAttribute"/>（若存在）。
    /// </summary>
    protected PluginBase ()
    {
        var attr = GetType().GetCustomAttribute<PluginAttribute>(inherit: false);
        ResolvedAttribute = attr;

        if (attr is not null)
        {
            _id = attr.Id;
            _name = attr.Name ?? GetType().Name;
            _version = attr.Version ?? "1.0.0";
            _category = attr.Category ?? "strategy";
        }
        else
        {
            _id = Guid.NewGuid().ToString();
            _name = GetType().Name;
            _version = "1.0.0";
            _category = "strategy";
            Debug.WriteLine($"警告：{GetType().FullName} 未标记 [Plugin] 特性，使用随机 GUID 作为 ID。");
        }
    }

    /// <inheritdoc />
    public string Id => _id;

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public string Version => _version;

    /// <inheritdoc />
    public string Category => _category;
}
