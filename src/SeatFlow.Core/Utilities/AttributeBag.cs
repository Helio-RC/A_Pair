using System.Collections.Concurrent;

namespace SeatFlow.Core.Utilities
{
    /// <summary>
    /// 扩展属性容器，以键值对形式存储任意类型的附加数据。
    /// 线程安全（基于 ConcurrentDictionary），供插件或自定义逻辑挂载额外信息。
    /// 例如：<see cref="Models.Student.Extensions"/> 和 <see cref="Models.Seat.Extensions"/>。
    /// </summary>
    public class AttributeBag
    {
        private readonly ConcurrentDictionary<string , object?> _store = new();

        /// <summary>
        /// 设置指定键的值。
        /// </summary>
        public void Set (string key , object? value) => _store[key] = value;

        /// <summary>
        /// 尝试获取指定键的值，并转换为指定类型。
        /// </summary>
        /// <typeparam name="T">期望的类型。</typeparam>
        /// <param name="key">键名。</param>
        /// <param name="value">输出值，失败时为 default。</param>
        /// <returns>是否成功获取并转换。</returns>
        public bool TryGet<T> (string key , out T? value)
        {
            if (_store.TryGetValue(key , out var obj) && obj is T t)
            {
                value = t;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 获取所有键值对。
        /// </summary>
        public IEnumerable<KeyValuePair<string , object?>> GetAll () => _store;
    }
}
