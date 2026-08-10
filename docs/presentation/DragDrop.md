# Avalonia 12 拖放实现指南

> 本文档记录了 SeatFlow 座位安排页面（`SeatingArrangementView`）的拖放实现模式和踩坑记录。
> Avalonia 版本：12.0.4

## 核心 API

### 发起拖放

唯一正确方式：在 `PointerPressed` 处理器中调用 `DragDrop.DoDragDropAsync`。

```csharp
private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (!ShouldDrag()) return;

    var data = new DataTransfer();
    var item = new DataTransferItem();
    item.Set(format, value);
    data.Add(item);

    var result = await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);

    // result != None → 拖放完成，不触发点击
    // result == None → 用户没有拖动，当作点击处理
    if (result != DragDropEffects.None) return;

    HandleClick();
}
```

**关键点：**
- `DoDragDropAsync` 是阻塞异步方法 — 用户松开鼠标或取消拖放后才返回
- 返回值区分拖放（`Move`/`Copy`）和点击（`None`），无需手动阈值检测
- **不要在 `PointerMoved` 中调用** — 事件已过按压阶段，内部手势检测无法工作

### 接收放置

目标元素**必须**设置 `DragDrop.AllowDrop="True"`，否则拖放事件（`DragOver`/`Drop`）根本不会传递到该元素。

```xml
<Border DragDrop.AllowDrop="True"
        DragDrop.DragOver="OnDragOver"
        DragDrop.Drop="OnDrop"
        DragDrop.DragLeave="OnDragLeave">
```

### 自定义数据格式

使用 `DataFormat.CreateInProcessFormat<T>` 创建进程内格式（同应用内拖放，不序列化出进程）：

```csharp
internal static class DragFormats
{
    public static readonly DataFormat<string> StudentDrag =
        DataFormat.CreateInProcessFormat<string>("SeatFlow_Student");
    public static readonly DataFormat<string> SeatDrag =
        DataFormat.CreateInProcessFormat<string>("SeatFlow_Seat");
}
```

| 方法 | 说明 |
|------|------|
| `CreateInProcessFormat<T>("name")` | 进程内格式，同应用内传输引用，性能最优 |
| `CreateStringApplicationFormat("app.name")` | 应用程序格式，可跨进程（但格式名需包含 `.`） |

## 数据读写

### 写入（发起方）

```csharp
var data = new DataTransfer();
var item = new DataTransferItem();
item.Set(DragFormats.StudentDrag, studentId);   // DataFormat<string>, string?
data.Add(item);
```

:::warning
`DataTransfer.Set()` 方法在 Avalonia 12.0.4 中**不存在**（文档中有但实际缺失）。必须用 `DataTransferItem`。
:::

### 读取（接收方）

```csharp
// 检查格式是否存在
private static bool DragHasFormat(IDataTransfer transfer, DataFormat format)
    => transfer.Items.Any(item => item.Formats.Contains(format));

// 读取字符串数据
private static string? DragGetString(IDataTransfer transfer, DataFormat<string> format)
{
    foreach (var item in transfer.Items)
    {
        if (item.Formats.Contains(format))
            return item.TryGetRaw(format) is string s ? s : null;
    }
    return null;
}
```

:::warning
`TryGetRaw(DataFormat)` 返回 `object?`，必须用 `is string s` 模式匹配解包。泛型重载 `TryGetRaw<T>(DataFormat<T>)` 不存在于 12.0.4。
:::

## 座位安排页面的交互模式

### 交互矩阵

| 操作 | 触发 | 数据 | 命令 |
|------|------|------|------|
| 点击已占座位 → 交换模式 | `PointerPressed` → click（`result == None`） | — | `ClickSeatCommand` |
| 拖动已占座位 → 空座位 | `PointerPressed` → drag | `StudentDrag` + `SeatDrag` | `SwapSeatCommand(dst=null)` |
| 拖动已占座位 → 已占座位 | `PointerPressed` → drag | `StudentDrag` + `SeatDrag` | `SwapSeatCommand` |
| 拖动列表学生 → 空座位 | `PointerPressed` → drag | `StudentDrag` | `AssignSeatCommand` |
| 拖动已占座位 → 垃圾桶 | `PointerPressed` → drag + `Trash_Drop` | `SeatDrag` | `RemoveStudentCommand` |
| 点击垃圾桶 | `Trash_PointerPressed` | — | `RemoveStudentCommand` |

### 座位 DragOver 验证逻辑

```csharp
private void Seat_DragOver(object? sender, DragEventArgs e)
{
    // 1. 先验证 sender 是座位 Border（不要在此之前设置 e.Handled）
    if (sender is not Border || DataContext is not SeatDisplayItem seat)
        return;  // 让事件继续冒泡

    // 2. 固定座位拒绝
    if (seat.IsFixed) { e.DragEffects = DragDropEffects.None; return; }

    // 3. 已占且不是从座位拖来的 → 拒绝（防止从未分配列表拖到已占位）
    if (seat.IsOccupied && !DragHasFormat(e.DataTransfer, DragFormats.SeatDrag))
    { e.DragEffects = DragDropEffects.None; return; }

    // 4. 不能拖到自己
    if (srcSeatId == seat.SeatId) { e.DragEffects = DragDropEffects.None; return; }

    // 5. 所有验证通过后设置"接受"状态
    seat.IsDragHover = true;
    e.DragEffects = DragDropEffects.Move;
    e.Handled = true;  // ← 最后才设置！
}
```

## CanvasZoomPan 交互

拖放与画布缩放/平移共用指针事件。冲突解决：

### 座位 Border 上的按压

1. `SeatBorder_PointerPressed` → 调用 `DoDragDropAsync`
2. `DoDragDropAsync` 内部消费指针事件 → `PointerPressed` 不再冒泡到 `ScrollViewer`
3. `CanvasZoomPan.OnPointerPressed` 不会收到事件

**兜底保护**（空座位等不调用 `DoDragDropAsync` 的情况）：

```csharp
// CanvasZoomPan.OnPointerPressed
if (e.Source is StyledElement src && src.DataContext is SeatDisplayItem)
{
    sv.SetValue(PanOriginProperty, new Point(double.NaN, double.NaN));
    return;  // 跳过平移
}

// CanvasZoomPan.OnPointerMoved
if (double.IsNaN(origin.X)) return;  // 哨兵检查，防止默认 (0,0) 触发平移
```

## 常见坑

| 问题 | 现象 | 原因 | 修复 |
|------|------|------|------|
| 拖不动 | 按下拖动无反应 | 使用 `PointerMoved` 启动拖放 | 改用 `PointerPressed` + `DoDragDropAsync` |
| 禁止光标 | 鼠标显示禁止符号 | 目标缺少 `AllowDrop="True"`，或 `e.Handled=true` 过早 + `DragEffects` 保持 `None` | 加 `AllowDrop`，`e.Handled` 移到验证后 |
| 拖不到垃圾桶 | 拖到垃圾桶上无反应 | 垃圾桶缺少 `AllowDrop="True"` | 加 `AllowDrop` |
| 拖动时画布乱移 | 座位拖动时画布同时平移 | `CanvasZoomPan` 未检测到座位拖放；`PanOrigin` 为 `(0,0)` 误触发 | NaN 哨兵 + `OnPointerMoved` 检查 |
| `DataTransfer.Set()` 不存在 | CS1929 | 文档和实际 API 版本不一致 | 用 `DataTransferItem.Set()` + `DataTransfer.Add()` |
| `TryGetRaw` 返回 `object?` | 类型不匹配 | 泛型重载不存在 | 用 `is string s` 模式匹配 |
| `e.Handled` 导致子元素事件丢失 | 点击不触发点击命令 | `PointerPressed` 中设置 `e.Handled = true` 阻止事件冒泡 | 不设置 `e.Handled`，让系统处理 |
