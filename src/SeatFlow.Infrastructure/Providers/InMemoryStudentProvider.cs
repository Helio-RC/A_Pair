using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// 内存中的学生数据提供器，用于测试或演示场景。
    /// </summary>
    /// <remarks>
    /// 忽略 <c>source</c> 参数，始终返回三个预定义的示例学生（Alice、Bob、Charlie）。
    /// 适用于快速原型开发或单元测试。
    /// </remarks>
    public class InMemoryStudentProvider : IStudentProvider
    {
        /// <inheritdoc />
        public Task<List<Student>> LoadAsync (string source , CancellationToken cancellationToken = default)
        {
            var list = new List<Student>
            {
                new() { Name = "Alice" },
                new() { Name = "Bob" },
                new() { Name = "Charlie" }
            };
            return Task.FromResult(list);
        }
    }
}
