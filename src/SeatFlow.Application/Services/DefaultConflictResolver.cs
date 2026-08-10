using SeatFlow.Application.Interfaces;
using SeatFlow.Core.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SeatFlow.Application.Services
{
    public class DefaultConflictResolver (ILogger<DefaultConflictResolver>? logger = null) : IConflictResolver
    {
        private readonly ILogger<DefaultConflictResolver> _logger = logger ?? NullLogger<DefaultConflictResolver>.Instance;

        /// <inheritdoc />
        public ConflictResolutionResult Resolve (SeatingWorkspace workspace)
        {
            var result = new ConflictResolutionResult { Success = true };
            var assignments = workspace.BuildSeatingPlan().Assignments;

            // 1. 检测重复分配（同一学生出现在多个座位）
            var studentToSeats = assignments.GroupBy(kv => kv.Value)
                .Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key , g => g.Select(kv => kv.Key).ToList());

            foreach (var dup in studentToSeats)
            {
                result.Conflicts.Add(new ConflictInfo
                {
                    Type = ConflictType.DuplicateAssignment ,
                    StudentId = dup.Key ,
                    Description = $"学生 {dup.Key} 被分配到多个座位: {string.Join(", " , dup.Value)}"
                });

                // 解决：保留第一个分配，清除其余
                var seatsToClear = dup.Value.Skip(1).ToList();
                foreach (var seatId in seatsToClear)
                {
                    var seat = workspace.FindSeats(s => s.Id == seatId).FirstOrDefault();
                    if (seat != null)
                    {
                        seat.OccupantId = null;
                        seat.IsAvailable = true;
                        result.ActionsTaken.Add($"清除座位 {seatId} 的重复分配");
                    }
                }
            }

            // 2. 检测固定座位冲突（固定座位分配的学生与配置不符）
            var fixedSeats = workspace.FindSeats(s => s.IsFixed).ToList();
            foreach (var seat in fixedSeats)
            {
                // 此处假设固定座位配置已在策略中应用，这里仅做验证
                if (string.IsNullOrEmpty(seat.OccupantId))
                {
                    result.Conflicts.Add(new ConflictInfo
                    {
                        Type = ConflictType.FixedSeatMismatch ,
                        SeatId = seat.Id ,
                        Description = $"固定座位 {seat.Id} 未分配学生"
                    });
                }
            }

            // 3. 检查座位容量（每个座位默认容量为1，已通过 OccupantId 保证）
            // 可扩展

            result.Success = result.Conflicts.Count == 0;
            if (result.Conflicts.Count > 0)
                _logger.LogInformation("冲突检测发现 {Count} 个冲突" , result.Conflicts.Count);
            else
                _logger.LogDebug("冲突检测完成，无冲突");
            return result;
        }
    }
}