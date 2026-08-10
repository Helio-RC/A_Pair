using System.Reflection;
using System.Runtime.Loader;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件程序集加载上下文，用于隔离加载插件程序集及其依赖项。
    /// </summary>
    /// <remarks>
    /// 继承自 <see cref="AssemblyLoadContext"/> 并设置 <c>isCollectible: true</c>，
    /// 支持在运行时卸载插件程序集以释放资源。使用 <see cref="AssemblyDependencyResolver"/>
    /// 解析插件程序集的依赖项路径，确保依赖项从插件目录而非主应用程序目录加载。
    /// </remarks>
    /// <param name="pluginPath">插件程序集文件的完整路径。</param>
    public class PluginLoadContext (string pluginPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        /// <summary>
        /// 解析并加载插件程序集所引用的程序集。
        /// </summary>
        /// <param name="assemblyName">要加载的程序集名称。</param>
        /// <returns>加载的程序集；如果无法解析则返回 <c>null</c>（回退到默认加载上下文）。</returns>
        protected override Assembly? Load (AssemblyName assemblyName)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
            return null;
        }
    }
}
