using System.IO.Compression;
using System.Text.Json;
using SeatFlow.Application.Scripting.CSharp;
using SeatFlow.Application.Scripting.Lua;
using SeatFlow.Contracts.Interfaces;
using SeatFlow.Core.Models;
using SeatFlow.Core.Services;
using SeatFlow.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Plugins
{
    /// <summary>
    /// 插件管理器，负责从指定目录发现、加载和管理插件包。
    /// 使用 <c>plugins-manifest.json</c> + 策略 <c>manifest.json</c> 双层清单架构。
    /// </summary>
    /// <remarks>
    /// <para>加载方式：</para>
    /// <list type="bullet">
    ///   <item><b>程序集插件（Assembly）</b> — 编译为 .dll 的程序集，通过 <see cref="PluginLoadContext"/> 隔离加载</item>
    ///   <item><b>脚本插件（Script）</b> — Lua 或 C# 脚本文件，通过适配器加载</item>
    /// </list>
    /// <para>加载时自动检测 <see cref="IPluginLifecycle"/> 并调用 <see cref="IPluginLifecycle.InitializeAsync"/>。
    /// 卸载时调用 <see cref="IPluginLifecycle.DisposeAsync"/> 并强制垃圾回收释放程序集资源。</para>
    /// </remarks>
    public class PluginManager : IPluginManager
    {
        private readonly string _pluginsPath;
        private readonly ILogger<PluginManager> _logger;
        private readonly List<LoadedContext> _contexts = [];
        private readonly Dictionary<string , Func<string , string , string , string , int , IPluginSeatingStrategy>> _scriptAdapters = new(StringComparer.OrdinalIgnoreCase);

        // 包级存储
        private readonly Dictionary<string , LoadedPackageInfo> _loadedPackages = [];
        private readonly Dictionary<string , string> _strategyToPackage = []; // strategyId → packageId
        private readonly Dictionary<string , PluginEntry> _strategyEntryMap = []; // strategyId → entry
        private readonly Dictionary<string , LoadedPluginInfo> _strategyPlugins = []; // strategyId → LoadedPluginInfo
        private readonly HashSet<string> _loadedPackageDirs = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 初始化插件管理器，确保插件目录存在并注册内置脚本适配器。
        /// </summary>
        public PluginManager (string pluginsPath , ILogger<PluginManager> logger)
        {
            _pluginsPath = pluginsPath;
            _logger = logger;
            Directory.CreateDirectory(_pluginsPath);

            _scriptAdapters["lua"] = (code , strategyId , name , version , priority) => new LuaScriptStrategy(code , strategyId , version , new LuaScriptConfiguration
            {
                StrategyName = name ,
                Priority = priority ,
                Enabled = true
            });
            _scriptAdapters["csharp"] = (code , strategyId , name , version , priority) => new CSharpScriptStrategy(code , strategyId , version , new CSharpScriptConfiguration
            {
                StrategyName = name ,
                Priority = priority ,
                Enabled = true
            });
        }

        // ─── IPluginManager 实现 ───

        /// <inheritdoc />
        public IReadOnlyDictionary<string , LoadedPackageInfo> LoadedPackages => _loadedPackages;

        /// <inheritdoc />
        public void RegisterScriptAdapter (string scriptType , Func<string , string , string , string , int , IPluginSeatingStrategy> factory)
        {
            _scriptAdapters[scriptType] = factory;
        }

        /// <inheritdoc />
        public Task<IEnumerable<LoadedPluginInfo>> LoadStrategyPluginsAsync (CancellationToken ct = default)
        {
            return LoadPluginsAsync("strategy" , ct);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<LoadedPluginInfo>> LoadPluginsAsync (string? category = null , CancellationToken ct = default)
        {
            if (!Directory.Exists(_pluginsPath))
                return [];

            var allStrategies = new List<LoadedPluginInfo>();
            if (_strategyPlugins.Count > 0)
                allStrategies.AddRange(_strategyPlugins.Values);

            foreach (var pluginDir in Directory.EnumerateDirectories(_pluginsPath))
            {
                ct.ThrowIfCancellationRequested();

                if (_loadedPackageDirs.Contains(pluginDir))
                    continue;

                try
                {
                    var manifestPath = Path.Combine(pluginDir , "plugins-manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var strategies = await LoadPackageInternal(pluginDir , manifestPath , category , ct);
                        allStrategies.AddRange(strategies);
                        _loadedPackageDirs.Add(pluginDir);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "加载插件包失败：{PluginDir}" , pluginDir);
                }
            }

            return allStrategies;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<LoadedPluginInfo>> RefreshPluginsAsync (string? category = null , CancellationToken ct = default)
        {
            await UnloadAllAsync();
            return await LoadPluginsAsync(category , ct);
        }

        /// <inheritdoc />
        public LoadedPluginInfo? GetLoadedPlugin (string pluginId)
        {
            _strategyPlugins.TryGetValue(pluginId , out var info);
            return info;
        }

        /// <inheritdoc />
        public (LoadedPackageInfo? Package , LoadedPluginInfo? Plugin) FindStrategy (string strategyId)
        {
            if (_strategyToPackage.TryGetValue(strategyId , out var packageId) &&
                _loadedPackages.TryGetValue(packageId , out var pkg) &&
                pkg.Strategies.TryGetValue(strategyId , out var plugin))
            {
                return (pkg , plugin);
            }
            return (null , null);
        }

        /// <inheritdoc />
        public async Task<LoadedPackageInfo?> LoadPackageAsync (string packageId , CancellationToken ct = default)
        {
            if (_loadedPackages.TryGetValue(packageId , out var cached))
                return cached;

            var packageDir = Path.Combine(_pluginsPath , packageId);
            if (!Directory.Exists(packageDir))
                return null;

            var manifestPath = Path.Combine(packageDir , "plugins-manifest.json");
            if (File.Exists(manifestPath))
            {
                await LoadPackageInternal(packageDir , manifestPath , null , ct);
                _loadedPackageDirs.Add(packageDir);
                return _loadedPackages.GetValueOrDefault(packageId);
            }

            return null;
        }

        /// <inheritdoc />
        public async Task RefreshPackageAsync (string packageId , CancellationToken ct = default)
        {
            var pkgInfo = await UnloadPackageInternalAsync(packageId);
            if (pkgInfo == null) return;

            var packageDir = pkgInfo.PackagePath;
            var manifestPath = Path.Combine(packageDir , "plugins-manifest.json");
            if (File.Exists(manifestPath))
                await LoadPackageInternal(packageDir , manifestPath , null , ct);

            _loadedPackageDirs.Add(packageDir);
        }

        /// <inheritdoc />
        public async Task UnloadPackageAsync (string packageId)
        {
            var pkgInfo = await UnloadPackageInternalAsync(packageId);
            if (pkgInfo == null) return;

            if (Directory.Exists(pkgInfo.PackagePath))
            {
                try { Directory.Delete(pkgInfo.PackagePath , recursive: true); }
                catch (Exception ex) { _logger.LogWarning(ex , "删除插件包目录失败：{Path}" , pkgInfo.PackagePath); }
            }
        }

        /// <summary>
        /// 卸载指定包：dispose 生命周期 → 移除内部字典强引用 → 卸载 AssemblyLoadContext。
        /// 返回包信息（供调用方继续使用包目录等）；包未加载时返回 null。
        /// </summary>
        private async Task<LoadedPackageInfo?> UnloadPackageInternalAsync (string packageId)
        {
            if (!_loadedPackages.TryGetValue(packageId , out var pkgInfo))
                return null;

            await UnloadSinglePackageInternal(pkgInfo);
            // 先移除内部字典的强引用（指向 ALC 内类型/实例），再卸载 ALC，否则运行时持强句柄阻止回收
            RemovePackageFromDictionaries(packageId);
            UnloadContexts(packageId);
            return pkgInfo;
        }

        /// <inheritdoc />
        public async Task SetPackageEnabledAsync (string packageId , bool enabled , CancellationToken ct = default)
        {
            if (!_loadedPackages.TryGetValue(packageId , out var pkgInfo))
                throw new InvalidOperationException($"插件包 {packageId} 未加载");

            var enables = pkgInfo.Enables ?? await LoadEnablesAsync(packageId , ct);
            enables.Enabled = enabled;
            pkgInfo.Enables = enables;

            await SaveEnablesAsync(packageId , enables , ct);

            foreach (var (strategyId , pluginInfo) in pkgInfo.Strategies)
            {
                var strategyEnabled = enabled && enables.Strategies.GetValueOrDefault(strategyId , true);
                pluginInfo.Strategy.IsEnabled = strategyEnabled;
            }
        }

        /// <inheritdoc />
        public async Task SetStrategyEnabledAsync (string strategyId , bool enabled , CancellationToken ct = default)
        {
            var (pkg , plugin) = FindStrategy(strategyId);
            if (pkg == null || plugin == null)
                throw new InvalidOperationException($"策略 {strategyId} 未找到");

            plugin.Strategy.IsEnabled = enabled;

            var enables = pkg.Enables ?? await LoadEnablesAsync(pkg.PackageManifest.Id , ct);
            enables.Strategies[strategyId] = enabled;
            pkg.Enables = enables;
            await SaveEnablesAsync(pkg.PackageManifest.Id , enables , ct);
        }

        /// <inheritdoc />
        public async Task<PluginEnables> LoadEnablesAsync (string packageId , CancellationToken ct = default)
        {
            var enablesPath = Path.Combine(_pluginsPath , packageId , "data" , "enables.json");
            if (!File.Exists(enablesPath))
                return new PluginEnables();

            var json = await File.ReadAllTextAsync(enablesPath , ct);
            return JsonSerializer.Deserialize<PluginEnables>(json ,
                JsonOptions.CaseInsensitiveRead) ?? new PluginEnables();
        }

        /// <inheritdoc />
        public async Task SaveEnablesAsync (string packageId , PluginEnables enables , CancellationToken ct = default)
        {
            var dataDir = Path.Combine(_pluginsPath , packageId , "data");
            Directory.CreateDirectory(dataDir);

            var enablesPath = Path.Combine(dataDir , "enables.json");
            var json = JsonSerializer.Serialize(enables , JsonOptions.WriteIndentedCamelCase);
            await File.WriteAllTextAsync(enablesPath , json , ct);
        }

        /// <inheritdoc />
        public async Task<string> InstallFromPackageAsync (string packagePath , CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(packagePath))
                throw new FileNotFoundException($"插件包文件不存在：{packagePath}");

            ValidateZipSafety(packagePath);

            var tempDir = Path.Combine(Path.GetTempPath() , $"apair_plugin_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(packagePath , tempDir , overwriteFiles: true);
                ct.ThrowIfCancellationRequested();

                // 防嵌套：若恰好只有 1 个目录 + 0 个文件，剥离外层
                var entries = Directory.GetFileSystemEntries(tempDir);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    var innerDir = entries[0];
                    foreach (var item in Directory.GetFileSystemEntries(innerDir))
                    {
                        var dest = Path.Combine(tempDir , Path.GetFileName(item));
                        if (Directory.Exists(item))
                            CopyDirectoryRecursive(item , dest);
                        else
                            File.Copy(item , dest , overwrite: true);
                    }
                    Directory.Delete(innerDir , recursive: true);
                }

                ct.ThrowIfCancellationRequested();

                // 读取包清单
                var manifestPath = Path.Combine(tempDir , "plugins-manifest.json");
                if (!File.Exists(manifestPath))
                    throw new InvalidDataException("插件包内缺少 plugins-manifest.json 文件");

                var json = await File.ReadAllTextAsync(manifestPath , ct);
                var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json ,
                    JsonOptions.CaseInsensitiveRead);
                if (manifest is null || string.IsNullOrEmpty(manifest.Id))
                    throw new InvalidDataException("plugins-manifest.json 格式无效：缺少 id 字段");
                var packageId = manifest.Id;

                // 安全校验：packageId 来自不可信清单，必须是合法单目录名（防路径遍历写入插件目录之外）
                var idError = SeatFlow.Contracts.Utilities.PluginArchiveSafety.ValidateSafePathSegment(packageId);
                if (idError != null)
                    throw new InvalidDataException($"plugins-manifest.json 的 id 字段无效：{idError}");

                ct.ThrowIfCancellationRequested();

                var targetDir = Path.Combine(_pluginsPath , packageId);
                if (Directory.Exists(targetDir))
                    throw new InvalidDataException($"插件包 \"{packageId}\" 已存在，请先卸载后再安装");

                Directory.CreateDirectory(targetDir);

                foreach (var filePath in Directory.EnumerateFiles(tempDir , "*" , SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(tempDir , filePath);
                    var destPath = Path.Combine(targetDir , relativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null)
                        Directory.CreateDirectory(destDir);
                    File.Copy(filePath , destPath , overwrite: false);
                }

                // 自动创建 data/enables.json（默认全部启用）
                var enables = new PluginEnables { Enabled = true };
                await SaveEnablesAsync(packageId , enables , ct);

                _logger.LogInformation("插件包 \"{PackageId}\" 安装成功：{TargetDir}" , packageId , targetDir);
                return targetDir;
            }
            finally
            {
                try { Directory.Delete(tempDir , recursive: true); }
                catch { /* 忽略清理失败 */ }
            }
        }

        /// <inheritdoc />
        public async Task UnloadAllAsync ()
        {
            foreach (var (_ , pkgInfo) in _loadedPackages)
            {
                await UnloadSinglePackageInternal(pkgInfo);
            }

            _loadedPackages.Clear();
            _strategyToPackage.Clear();
            _strategyEntryMap.Clear();
            _strategyPlugins.Clear();
            _loadedPackageDirs.Clear();

            UnloadContexts(packageId: null);
        }

        // ─── 私有方法 ───

        /// <summary>
        /// 加载插件包（<c>plugins-manifest.json</c> + 策略 <c>manifest.json</c>）。
        /// </summary>
        private async Task<List<LoadedPluginInfo>> LoadPackageInternal (
            string packageDir , string manifestPath , string? category , CancellationToken ct)
        {
            var results = new List<LoadedPluginInfo>();

            var manifestJson = await File.ReadAllTextAsync(manifestPath , ct);
            var packageManifest = JsonSerializer.Deserialize<PluginPackageManifest>(manifestJson ,
                JsonOptions.CaseInsensitiveRead);
            if (packageManifest == null || string.IsNullOrEmpty(packageManifest.Id))
                return results;

            if (category != null && !string.Equals(packageManifest.Type , category , StringComparison.OrdinalIgnoreCase))
                return results;

            var packageId = packageManifest.Id;

            var enables = await LoadEnablesAsync(packageId , ct);
            if (string.IsNullOrEmpty(enables.Type))
                enables.Type = packageManifest.Type;

            var pkgInfo = new LoadedPackageInfo
            {
                PackageManifest = packageManifest ,
                PackagePath = packageDir ,
                Enables = enables
            };

            foreach (var entry in packageManifest.Plugins)
            {
                ct.ThrowIfCancellationRequested();

                // 按插件类型分派加载：当前仅 strategy 受支持，其余类型给出警告并跳过（预留扩展点）
                if (!string.Equals(entry.Kind , PluginKind.Strategy , StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("插件类型 {Kind}（包：{PkgId}）暂不支持，已跳过" , entry.Kind , packageId);
                    continue;
                }

                // 纵深防御：entry 路径字段来自不可信清单，拒绝路径遍历（../、绝对路径、分隔符）
                var pathError = SeatFlow.Contracts.Utilities.PluginArchiveSafety.ValidateSafePathSegment(entry.Path);
                if (pathError != null)
                {
                    _logger.LogWarning("插件条目 path 字段无效（包：{PkgId}）：{Error}" , packageId , pathError);
                    continue;
                }
                var manifestFieldError = SeatFlow.Contracts.Utilities.PluginArchiveSafety.ValidateSafeRelativePath(entry.Manifest);
                if (manifestFieldError != null)
                {
                    _logger.LogWarning("插件条目 manifest 字段无效（包：{PkgId}）：{Error}" , packageId , manifestFieldError);
                    continue;
                }

                var strategyManifestPath = Path.Combine(packageDir , entry.Manifest);
                if (!File.Exists(strategyManifestPath))
                {
                    _logger.LogWarning("策略 manifest 文件不存在：{Path}，包：{PkgId}" , strategyManifestPath , packageId);
                    continue;
                }

                var strategyManifestJson = await File.ReadAllTextAsync(strategyManifestPath , ct);
                var strategyManifest = JsonSerializer.Deserialize<StrategyManifest>(strategyManifestJson ,
                    JsonOptions.CaseInsensitiveRead);
                if (strategyManifest == null || string.IsNullOrEmpty(strategyManifest.Id))
                {
                    _logger.LogWarning("策略 manifest 无效：{Path}" , strategyManifestPath);
                    continue;
                }

                ValidatePluginManifestVersion(strategyManifest);

                var strategy = await LoadStrategyFromEntry(entry , strategyManifest , packageDir , packageId , ct);
                if (strategy == null)
                    continue;

                strategy.Priority = strategyManifest.DefaultPriority;

                var isEnabled = enables.Enabled && enables.Strategies.GetValueOrDefault(strategyManifest.Id , true);
                strategy.IsEnabled = isEnabled;

                if (strategy is IPluginLifecycle lifecycle)
                {
                    var host = new PluginHost(_pluginsPath , packageDir);
                    await lifecycle.InitializeAsync(host , ct);
                }

                var pluginInfo = new LoadedPluginInfo
                {
                    Strategy = strategy ,
                    PluginPath = packageDir ,
                    Entry = entry ,
                    StrategyManifest = strategyManifest
                };

                pkgInfo.Strategies[strategyManifest.Id] = pluginInfo;
                _strategyToPackage[strategyManifest.Id] = packageId;
                _strategyEntryMap[strategyManifest.Id] = entry;
                _strategyPlugins[strategyManifest.Id] = pluginInfo;
                results.Add(pluginInfo);

                _logger.LogDebug("加载插件策略：{StrategyId}（包：{PkgId}，类型：{LoadKind}）" ,
                    strategyManifest.Id , packageId ,
                    !string.IsNullOrEmpty(entry.Assembly) ? "assembly" :
                    !string.IsNullOrEmpty(entry.ScriptFile) ? entry.ScriptType ?? "script" : "unknown");
            }

            // 包即使没有任何成功加载的策略也注册（如全部为未支持的插件类型），
            // 否则 UI 无法定位并卸载该包
            _loadedPackages[packageId] = pkgInfo;
            return results;
        }

        /// <summary>
        /// 从 PluginEntry 和 StrategyManifest 加载策略实例。
        /// </summary>
        private async Task<IPluginSeatingStrategy?> LoadStrategyFromEntry (
            PluginEntry entry , StrategyManifest strategyManifest , string packageDir , string packageId , CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(entry.ScriptFile) && !string.IsNullOrEmpty(entry.ScriptType))
            {
                // 纵深防御：脚本文件路径来自不可信清单
                var scriptFieldError = SeatFlow.Contracts.Utilities.PluginArchiveSafety.ValidateSafeRelativePath(entry.ScriptFile);
                if (scriptFieldError != null)
                {
                    _logger.LogWarning("插件条目 scriptFile 字段无效：{Error}" , scriptFieldError);
                    return null;
                }

                var scriptPath = Path.Combine(packageDir , entry.Path , entry.ScriptFile);
                if (!File.Exists(scriptPath))
                {
                    scriptPath = Path.Combine(packageDir , entry.ScriptFile);
                    if (!File.Exists(scriptPath))
                    {
                        _logger.LogWarning("脚本文件不存在：{ScriptFile}" , entry.ScriptFile);
                        return null;
                    }
                }

                var scriptCode = await File.ReadAllTextAsync(scriptPath , ct);

                if (_scriptAdapters.TryGetValue(entry.ScriptType , out var factory))
                    return factory(scriptCode , strategyManifest.Id , strategyManifest.DisplayName ,
                        strategyManifest.Version , strategyManifest.DefaultPriority);

                _logger.LogWarning("未找到脚本适配器：{ScriptType}，策略：{StrategyId}" , entry.ScriptType , strategyManifest.Id);
                return null;
            }
            else if (!string.IsNullOrEmpty(entry.Assembly) && !string.IsNullOrEmpty(entry.EntryType))
            {
                // 纵深防御：程序集路径来自不可信清单
                var assemblyFieldError = SeatFlow.Contracts.Utilities.PluginArchiveSafety.ValidateSafeRelativePath(entry.Assembly);
                if (assemblyFieldError != null)
                {
                    _logger.LogWarning("插件条目 assembly 字段无效：{Error}" , assemblyFieldError);
                    return null;
                }

                var assemblyPath = Path.Combine(packageDir , entry.Path , entry.Assembly);
                if (!File.Exists(assemblyPath))
                {
                    assemblyPath = Path.Combine(packageDir , entry.Assembly);
                    if (!File.Exists(assemblyPath))
                    {
                        _logger.LogWarning("程序集文件不存在：{Assembly}" , entry.Assembly);
                        return null;
                    }
                }

                var loadContext = new PluginLoadContext(assemblyPath);
                var loadedContext = new LoadedContext(loadContext , packageId);
                _contexts.Add(loadedContext);

                bool succeeded = false;
                try
                {
                    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                    var type = assembly.GetType(entry.EntryType);
                    if (type == null || !typeof(IPluginSeatingStrategy).IsAssignableFrom(type))
                    {
                        _logger.LogWarning("入口类型 {Type} 不存在或未实现 IPluginSeatingStrategy" , entry.EntryType);
                        return null;
                    }

                    var instance = Activator.CreateInstance(type) as IPluginSeatingStrategy;
                    succeeded = instance != null;
                    return instance;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "插件策略实例化失败：{Type}（包：{PkgId}）" , entry.EntryType , packageId);
                    return null;
                }
                finally
                {
                    // 失败路径（类型不符/实例化异常）：卸载并移除刚创建的上下文，避免 AssemblyLoadContext 泄漏
                    if (!succeeded)
                    {
                        _contexts.Remove(loadedContext);
                        try { loadContext.Unload(); }
                        catch (Exception ex) { _logger.LogWarning(ex , "卸载失败路径的插件上下文异常：{Type}" , entry.EntryType); }
                    }
                }
            }

            _logger.LogWarning("策略 {StrategyId} 缺少加载指令（assembly/entryType 或 scriptFile/scriptType）" , strategyManifest.Id);
            return null;
        }

        /// <summary>
        /// 卸载单个包内的所有策略（dispose lifecycle）。
        /// </summary>
        private async Task UnloadSinglePackageInternal (LoadedPackageInfo pkgInfo)
        {
            foreach (var (_ , pluginInfo) in pkgInfo.Strategies)
            {
                if (pluginInfo.Strategy is IPluginLifecycle lifecycle)
                {
                    try
                    {
                        await lifecycle.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex , "插件 DisposeAsync 失败：{Id}" , pluginInfo.Strategy.Id);
                    }
                }
            }
        }

        /// <summary>
        /// 校验插件策略 manifest 版本：高于当前程序支持的最大版本时警告（仍加载，兼容模式）。
        /// 与内置 manifest 的版本校验（<see cref="SeatFlow.Core.Services.StrategyManifestProvider"/>）行为一致。
        /// </summary>
        /// <param name="strategyManifest">插件策略 manifest。</param>
        private void ValidatePluginManifestVersion (StrategyManifest strategyManifest)
        {
            var version = strategyManifest.ManifestVersion;
            if (string.IsNullOrEmpty(version)) return;

            if (StrategyManifestProvider.CompareVersions(version ,
                    StrategyManifestProvider.MaxManifestVersion) > 0)
            {
                _logger.LogWarning(
                    "插件策略 Manifest 版本 {ManifestVersion} 高于当前程序支持的最大版本 {MaxVersion}，" +
                    "策略 {StrategyId} 可能包含不受支持的字段，将以兼容模式加载" ,
                    version , StrategyManifestProvider.MaxManifestVersion ,
                    strategyManifest.Id);
            }
        }

        /// <summary>
        /// 卸载指定包（或全部，packageId 为 null）的 AssemblyLoadContext，
        /// 并按官方模式以弱引用循环探测回收完成度。
        /// </summary>
        /// <remarks>
        /// <b>调用前置条件：</b>调用前必须已从 <c>_strategyPlugins</c> 等字典移除指向
        /// ALC 内类型/实例的强引用（<see cref="RemovePackageFromDictionaries"/>），
        /// 否则运行时对 ALC 持有的强 GC 句柄会阻止回收。
        /// </remarks>
        /// <param name="packageId">包 ID；为 null 时卸载所有上下文。</param>
        private void UnloadContexts (string? packageId)
        {
            List<LoadedContext> toUnload;
            if (packageId == null)
            {
                toUnload = [.. _contexts];
                _contexts.Clear();
            }
            else
            {
                toUnload = [.. _contexts.Where(c => c.PackageId == packageId)];
                foreach (var lc in toUnload)
                    _contexts.Remove(lc);
            }

            foreach (var lc in toUnload)
            {
                try { lc.Context.Unload(); }
                catch (Exception ex) { _logger.LogWarning(ex , "插件上下文卸载失败：{PackageId}" , lc.PackageId); }
            }

            // 官方卸载模式：Unload() 仅发起，需多轮 GC 循环等待真正回收。
            // 注意：必须使用压缩式强制收集（compacting）——collectible ALC 的 LoaderAllocator
            // 仅在压缩 GC 中释放，普通 GC.Collect() 无法完成回收。
            for (int round = 0; round < 10 && toUnload.Any(lc => lc.WeakRef.IsAlive); round++)
            {
                GC.Collect(2 , GCCollectionMode.Forced , blocking: true , compacting: true);
                GC.WaitForPendingFinalizers();
            }

            var stillAlive = toUnload.Where(lc => lc.WeakRef.IsAlive).ToList();
            if (stillAlive.Count > 0)
            {
                _logger.LogWarning("插件上下文未能完全卸载，可能存在引用泄漏：{PackageId}（{Count} 个上下文）" ,
                    packageId ?? "<all>" , stillAlive.Count);
            }
        }

        /// <summary>
        /// 验证 ZIP 文件安全性：检查压缩炸弹、总大小、条目数、路径遍历。
        /// 实现收敛于 <see cref="SeatFlow.Contracts.Utilities.PluginArchiveSafety"/>（宿主与 SDK 共享，
        /// 避免双份逻辑漂移）。
        /// </summary>
        private static void ValidateZipSafety (string archivePath)
            => SeatFlow.Contracts.Utilities.PluginArchiveSafety.EnsureSafe(archivePath);

        /// <summary>
        /// 递归复制目录（Copy+Delete 模式，兼容跨文件系统场景）。
        /// </summary>
        private static void CopyDirectoryRecursive (string sourceDir , string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir , Path.GetFileName(file));
                File.Copy(file , destFile , overwrite: true);
            }
            foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir , Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir , destSubDir);
            }
        }

        /// <summary>
        /// 从所有内部字典中移除指定包。
        /// </summary>
        private void RemovePackageFromDictionaries (string packageId)
        {
            if (_loadedPackages.TryGetValue(packageId , out var pkgInfo))
            {
                _loadedPackageDirs.Remove(pkgInfo.PackagePath);
                foreach (var strategyId in pkgInfo.Strategies.Keys)
                {
                    _strategyToPackage.Remove(strategyId);
                    _strategyEntryMap.Remove(strategyId);
                    _strategyPlugins.Remove(strategyId);
                }
                _loadedPackages.Remove(packageId);
            }
        }
    }

    /// <summary>
    /// 表示已加载的插件信息，包含策略实例、加载条目和路径。
    /// </summary>
    public class LoadedPluginInfo
    {
        /// <summary>
        /// 策略实例。
        /// </summary>
        public IPluginSeatingStrategy Strategy { get; set; } = default!;

        /// <summary>
        /// 获取插件通用接口实例。当前隐式派生自 <see cref="Strategy"/>。
        /// </summary>
        public IPlugin Plugin => Strategy;

        /// <summary>
        /// 插件所在目录的绝对路径。
        /// </summary>
        public string PluginPath { get; set; } = string.Empty;

        /// <summary>
        /// 策略对应的加载条目（来自 <c>plugins-manifest.json</c> 的 <c>strategies[]</c>）。
        /// </summary>
        public PluginEntry? Entry { get; set; }

        /// <summary>
        /// 策略的声明式元数据清单（来自策略 <c>manifest.json</c>）。
        /// </summary>
        public StrategyManifest? StrategyManifest { get; set; }
    }

    /// <summary>
    /// 插件宿主的默认实现，在插件初始化时传递给 <see cref="IPluginLifecycle.InitializeAsync"/>。
    /// </summary>
    internal class PluginHost (string pluginsBasePath , string pluginDir) : IPluginHost
    {
        public IPluginConfigurationService Configuration { get; } = new PluginConfigurationService(pluginsBasePath);
        public string PluginDirectory { get; } = pluginDir;
    }

    /// <summary>
    /// 已加载的插件 AssemblyLoadContext 登记项：记录所属包与弱引用，
    /// 用于按包精准卸载与卸载完成度探测（<see cref="PluginManager.UnloadContexts"/>）。
    /// </summary>
    /// <param name="context">插件程序集加载上下文。</param>
    /// <param name="packageId">所属插件包 ID。</param>
    internal sealed class LoadedContext (PluginLoadContext context , string packageId)
    {
        /// <summary>插件程序集加载上下文。</summary>
        public PluginLoadContext Context { get; } = context;

        /// <summary>所属插件包 ID。</summary>
        public string PackageId { get; } = packageId;

        /// <summary>
        /// 上下文的弱引用（trackResurrection），卸载后用于循环探测回收完成度。
        /// </summary>
        public WeakReference WeakRef { get; } = new(context , trackResurrection: true);
    }
}
