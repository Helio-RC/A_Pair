using System.Text.Json;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using SeatFlow.Infrastructure.Serialization;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// JSON 格式的学生数据写入器，将学生列表以 <see cref="RosterFile"/> 格式序列化为 JSON 文件。
    /// </summary>
    /// <remarks>
    /// 输出文件使用 camelCase 命名策略，包含版本号和完整的学生列表。
    /// </remarks>
    public class JsonStudentWriter : IStudentWriter
    {
        /// <inheritdoc />
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            var roster = new RosterFile
            {
                Version = "1.1" ,
                Students = new List<Student>(students)
            };

            var options = JsonOptions.WriteIndentedCamelCase;

            var json = JsonSerializer.Serialize(roster , options);
            await File.WriteAllTextAsync(path , json , cancellationToken);
        }
    }
}