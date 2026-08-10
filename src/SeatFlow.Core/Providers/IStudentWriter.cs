using SeatFlow.Core.Models;

namespace SeatFlow.Core.Providers
{
    /// <summary>
    /// 学生数据写入器接口，定义将学生列表导出到文件的契约。
    /// 实现类包括 CSV、Excel (XLSX) 和 JSON 格式。
    /// </summary>
    public interface IStudentWriter
    {
        /// <summary>
        /// 将学生列表写入指定路径的文件。
        /// </summary>
        /// <param name="path">输出文件路径。</param>
        /// <param name="students">学生列表。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default);
    }
}