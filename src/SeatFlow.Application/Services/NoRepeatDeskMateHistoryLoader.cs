using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Strategies;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Services
{
    /// <summary>
    /// 同桌不重复历史加载器。从会场最近 N 个快照中提取过去的同桌对，
    /// 注入到 <see cref="NoRepeatDeskMateStrategy"/> 中，
    /// 使其在 RandomFill 分配循环中能检测并避免重复的同桌配对。
    /// </summary>
    internal class NoRepeatDeskMateHistoryLoader (
        ISeatingSnapshotRepository snapshotRepository ,
        ILogger<NoRepeatDeskMateHistoryLoader>? logger = null)
    {
        private readonly ISeatingSnapshotRepository _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        private readonly ILogger<NoRepeatDeskMateHistoryLoader> _logger = logger ?? NullLogger<NoRepeatDeskMateHistoryLoader>.Instance;

        /// <summary>
        /// 从会场最近 <paramref name="historyWindowSize"/> 个快照中提取过去的同桌对，
        /// 并注入到策略实例中。
        /// </summary>
        /// <param name="workspace">当前工作区（用于过滤有效学生 ID）。</param>
        /// <param name="venueId">会场 ID。</param>
        /// <param name="historyWindowSize">参考历史快照个数。</param>
        /// <param name="strategy">要填充的目标策略实例。</param>
        /// <param name="ct">取消令牌。</param>
        public async Task PopulateDeskMateHistoryAsync (
            SeatingWorkspace workspace ,
            string venueId ,
            int historyWindowSize ,
            NoRepeatDeskMateStrategy strategy ,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(strategy);
            if (string.IsNullOrEmpty(venueId))
            {
                _logger.LogDebug("未指定会场 ID，跳过同桌历史加载");
                return;
            }

            // 1. 加载该会场所有快照，取最近 historyWindowSize 个
            var snapshots = await _snapshotRepository.ListByVenueAsync(venueId , ct);
            var recentSnapshots = snapshots.Take(historyWindowSize).ToList();
            if (recentSnapshots.Count == 0)
            {
                _logger.LogDebug("会场 {VenueId} 无历史快照，同桌历史为空" , venueId);
                strategy.ClearHistory();
                return;
            }

            // 2. 对每个快照（从旧到新），解析嵌入布局 → 提取同桌对
            var allPairs = new HashSet<(string , string)>();
            int snapshotIndex = 0;
            foreach (var snapshot in Enumerable.Reverse(recentSnapshots))
            {
                snapshotIndex++;
                var pairs = ExtractDeskMatePairsFromSnapshot(snapshot);
                foreach (var pair in pairs)
                    allPairs.Add(pair);
                _logger.LogDebug(
                    "快照 {Index}（{SnapshotId}）：提取 {Count} 对历史同桌" ,
                    snapshotIndex , snapshot.Id , pairs.Count);
            }

            // 4. 注入到策略
            strategy.ClearHistory();
            strategy.SetPastDeskMatePairs(allPairs);

            _logger.LogDebug(
                "从 {SnapshotCount} 个快照中加载了 {TotalPairs} 对历史同桌记录（会场 {VenueId}）" ,
                recentSnapshots.Count , allPairs.Count , venueId);
        }

        /// <summary>
        /// 从单个快照中提取同桌对。
        /// 对每个几何相邻且桌边界匹配的座位对，若两个座位均有分配且学生存在于当前工作区，
        /// 则记录为同桌对。
        /// </summary>
        private HashSet<(string , string)> ExtractDeskMatePairsFromSnapshot (
            SeatingSnapshot snapshot)
        {
            // 提取嵌入的会场布局（兼容新旧两种元数据键）
            var layoutJson = SnapshotLayoutHelper.GetMetaStringFromMetadata(snapshot.Metadata , "venueFile")
                ?? SnapshotLayoutHelper.GetMetaStringFromMetadata(snapshot.Metadata , "venueLayout");
            if (string.IsNullOrEmpty(layoutJson))
            {
                _logger.LogDebug("快照 {SnapshotId} 无嵌入会场布局，跳过" , snapshot.Id);
                return [];
            }

            ClassroomLayoutDefinition? layout;
            try
            {
                layout = SnapshotLayoutHelper.DeserializeVenueFromEmbeddedJson(layoutJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "快照 {SnapshotId} 的嵌入布局反序列化失败，跳过" , snapshot.Id);
                return [];
            }

            if (layout?.Seats == null || layout.Seats.Count == 0)
            {
                _logger.LogDebug("快照 {SnapshotId} 的布局中无座位，跳过" , snapshot.Id);
                return [];
            }

            // 从布局元数据读取 SeatsPerDesk
            int seatsPerDesk = layout.Metadata is GridLayoutMetadata gm ? gm.SeatsPerDesk : 2;
            if (seatsPerDesk < 1) seatsPerDesk = 1;

            var seats = layout.Seats;
            var pairs = new HashSet<(string , string)>();

            // 遍历所有座位对，找出桌边界感知的相邻座位
            for (int i = 0; i < seats.Count; i++)
            {
                for (int j = i + 1; j < seats.Count; j++)
                {
                    if (!SeatAdjacencyHelper.AreDeskMates(seats[i] , seats[j] , seatsPerDesk))
                        continue;

                    var occA = snapshot.SeatAssignments.GetValueOrDefault(seats[i].Id);
                    var occB = snapshot.SeatAssignments.GetValueOrDefault(seats[j].Id);

                    if (string.IsNullOrEmpty(occA) || string.IsNullOrEmpty(occB))
                        continue;

                    // 规范化：确保 (A, B) 和 (B, A) 视为同一对
                    var pair = string.CompareOrdinal(occA , occB) <= 0
                        ? (occA , occB)
                        : (occB , occA);

                    pairs.Add(pair);
                }
            }

            return pairs;
        }
    }
}
