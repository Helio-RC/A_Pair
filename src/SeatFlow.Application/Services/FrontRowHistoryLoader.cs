using SeatFlow.Core.DomainServices;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Services;

/// <summary>
/// 前排轮换历史加载器。从该会场最近 N 个快照中恢复学生的前排座位历史，
/// 使 <see cref="Core.Strategies.FrontRowRotationStrategy"/> 的历史惩罚机制在跨会话场景下也能生效。
/// </summary>
internal class FrontRowHistoryLoader (
    ISeatingSnapshotRepository snapshotRepository ,
    ILogger<FrontRowHistoryLoader>? logger = null)
{
    private readonly ISeatingSnapshotRepository _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
    private readonly ILogger<FrontRowHistoryLoader> _logger = logger ?? NullLogger<FrontRowHistoryLoader>.Instance;

    /// <summary>
    /// 从该会场最近 <paramref name="historyWindowSize"/> 个快照中恢复学生的前排座位历史。
    /// 每个学生在快照中被分配到前排座位的记录会填入其 <see cref="Student.RecentSeatHistory"/>。
    /// </summary>
    /// <param name="workspace">当前工作区（含已加载的学生列表）。</param>
    /// <param name="venueId">会场 ID。</param>
    /// <param name="historyWindowSize">参考历史快照个数。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task PopulateFrontRowHistoryAsync (
        SeatingWorkspace workspace ,
        string venueId ,
        int historyWindowSize ,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrEmpty(venueId))
        {
            _logger.LogDebug("未指定会场 ID，跳过前排历史恢复");
            return;
        }

        // 1. 加载该会场所有快照，取最近 historyWindowSize 个
        var snapshots = await _snapshotRepository.ListByVenueAsync(venueId , ct);
        var recentSnapshots = snapshots.Take(historyWindowSize).ToList();
        if (recentSnapshots.Count == 0)
        {
            _logger.LogDebug("会场 {VenueId} 无历史快照，跳过前排历史恢复" , venueId);
            return;
        }

        // 2. 确保每个学生的缓冲区容量 ≥ historyWindowSize
        var studentMap = workspace.Students.ToDictionary(s => s.Id);
        foreach (var s in workspace.Students)
            s.RecentSeatHistory.Resize(historyWindowSize);

        // 3. 对每个快照（从旧到新），解析嵌入布局 → 识别前排 → 填充历史
        int restoredCount = 0;
        foreach (var snapshot in Enumerable.Reverse(recentSnapshots))
        {
            var layoutJson = SnapshotLayoutHelper.GetMetaStringFromMetadata(snapshot.Metadata , "venueFile")
                ?? SnapshotLayoutHelper.GetMetaStringFromMetadata(snapshot.Metadata , "venueLayout");
            if (string.IsNullOrEmpty(layoutJson))
            {
                _logger.LogDebug("快照 {SnapshotId} 无嵌入会场布局，跳过" , snapshot.Id);
                continue;
            }

            ClassroomLayoutDefinition? layout;
            try
            {
                layout = SnapshotLayoutHelper.DeserializeVenueFromEmbeddedJson(layoutJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "快照 {SnapshotId} 的嵌入布局反序列化失败，跳过" , snapshot.Id);
                continue;
            }

            if (layout == null)
            {
                _logger.LogDebug("快照 {SnapshotId} 反序列化布局为 null，跳过" , snapshot.Id);
                continue;
            }

            var frontSeatIds = IdentifyFrontRowSeats(layout);
            foreach (var (seatId , studentId) in snapshot.SeatAssignments)
            {
                if (string.IsNullOrEmpty(studentId)) continue;
                if (!frontSeatIds.Contains(seatId)) continue;
                if (!studentMap.TryGetValue(studentId , out var student)) continue;

                student.RecentSeatHistory.Add(seatId);
                restoredCount++;
            }
        }

        _logger.LogInformation("从 {SnapshotCount} 个快照恢复了 {EntryCount} 条前排历史记录" ,
            recentSnapshots.Count , restoredCount);
    }

    /// <summary>
    /// 识别会场布局中的前排座位 ID 集合。
    /// 逻辑与 <see cref="Core.Strategies.FrontRowRotationStrategy.ExecuteAsync"/> 保持一致。
    /// </summary>
    /// <param name="layout">会场布局定义。</param>
    /// <returns>前排座位 ID 的集合。</returns>
    public static HashSet<string> IdentifyFrontRowSeats (ClassroomLayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int frontRowCount = layout.Metadata switch
        {
            GridLayoutMetadata gm => gm.FrontRowCount,
            PolarLayoutMetadata pm => pm.FrontRowCount,
            _ => 1
        };
        return SeatGeometryHelper.IdentifyFrontRowSeats(layout.Seats , frontRowCount);
    }
}
