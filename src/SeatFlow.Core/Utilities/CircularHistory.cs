using System.Collections;

namespace SeatFlow.Core.Utilities;

/// <summary>
/// 环形缓冲区（Circular Buffer），用于存储固定容量的历史记录。
/// 当添加新元素超过容量时，自动覆盖最旧的元素。
/// 用于 <see cref="Models.Student.RecentSeatHistory"/>，记录学生最近坐过的座位。
/// </summary>
/// <typeparam name="T">存储的元素类型。</typeparam>
public class CircularHistory<T> : IEnumerable<T>
{
    private T[] _buffer;
    private int _index = 0;
    private int _count = 0;

    /// <summary>
    /// 创建指定容量的环形缓冲区。
    /// </summary>
    /// <param name="capacity">容量，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">容量小于等于 0 时抛出。</exception>
    public CircularHistory (int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = new T[capacity];
    }

    /// <summary>
    /// 添加一个元素到缓冲区。如果已满，覆盖最旧的元素。
    /// 若元素已存在于缓冲区中，则忽略本次添加（去重）。
    /// </summary>
    public void Add (T item)
    {
        // 去重：避免快照回滚等场景下同一元素重复出现
        if (_count > 0 && Contains(item)) return;

        _buffer[_index] = item;
        _index = (_index + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    /// <summary>
    /// 检查元素是否已存在于缓冲区中。
    /// </summary>
    private bool Contains (T item)
    {
        foreach (var existing in this)
        {
            if (EqualityComparer<T>.Default.Equals(existing , item))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 获取所有历史记录，按从旧到新的顺序排列。
    /// </summary>
    public IEnumerable<T> GetAll ()
    {
        return this;
    }

    /// <summary>
    /// 调整缓冲区容量。扩容时保留全部历史；缩容时仅保留最近 <paramref name="newCapacity"/> 条。
    /// </summary>
    /// <param name="newCapacity">新容量，必须大于 0。</param>
    /// <exception cref="ArgumentOutOfRangeException">容量小于等于 0 时抛出。</exception>
    public void Resize (int newCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newCapacity);
        if (newCapacity == _buffer.Length) return;

        var oldEntries = GetAll().ToList();
        _buffer = new T[newCapacity];
        _index = 0;
        _count = 0;

        // 缩容时只保留最新的 newCapacity 条；扩容时全部保留
        int start = Math.Max(0 , oldEntries.Count - newCapacity);
        for (int i = start; i < oldEntries.Count; i++)
            Add(oldEntries[i]);
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator ()
    {
        for (int i = 0; i < _count; i++)
        {
            int idx = (_index - _count + i) % _buffer.Length;
            if (idx < 0) idx += _buffer.Length;
            yield return _buffer[idx];
        }
    }

    IEnumerator IEnumerable.GetEnumerator () => GetEnumerator();
}
