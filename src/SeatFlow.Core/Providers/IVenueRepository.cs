using SeatFlow.Core.Models;

namespace SeatFlow.Core.Providers
{
    /// <summary>
    /// 会场仓储接口，定义会场布局数据的持久化契约。
    /// 每个会场对应一个 <see cref="ClassroomLayoutDefinition"/>，包含座位布局和障碍物信息。
    /// </summary>
    public interface IVenueRepository
    {
        /// <summary>保存会场布局。</summary>
        /// <param name="venueId">会场唯一标识符。</param>
        /// <param name="layout">教室布局定义。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task SaveAsync (string venueId , ClassroomLayoutDefinition layout , CancellationToken cancellationToken = default);

        /// <summary>加载会场布局。</summary>
        /// <param name="venueId">会场唯一标识符。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>教室布局定义，不存在时返回 null。</returns>
        Task<ClassroomLayoutDefinition?> LoadAsync (string venueId , CancellationToken cancellationToken = default);

        /// <summary>获取所有会场的 ID 列表。</summary>
        Task<IEnumerable<string>> ListVenueIdsAsync (CancellationToken cancellationToken = default);

        /// <summary>删除指定会场。</summary>
        Task DeleteAsync (string venueId , CancellationToken cancellationToken = default);
        /// <summary>获取会场文件的 ContentHash（轻量读取，不反序列化全量布局）。</summary>
        Task<string?> GetContentHashAsync (string venueId , CancellationToken ct = default);
        /// <summary>获取会场文件的原始 JSON 内容。</summary>
        Task<string?> GetRawVenueFileAsync (string venueId , CancellationToken ct = default);
    }
}