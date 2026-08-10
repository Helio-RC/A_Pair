using SeatFlow.Core.Models;

namespace SeatFlow.Core.Providers;

public interface IStudentDatasetRepository
{
    Task SaveAsync (string id , string name , List<Student> students ,
        string? originalFileName = null , CancellationToken ct = default);
    Task<List<Student>?> LoadAsync (string id , CancellationToken ct = default);
    Task<IReadOnlyList<StudentDatasetInfo>> ListAsync (CancellationToken ct = default);
    Task DeleteAsync (string id , CancellationToken ct = default);
    /// <summary>原地重命名数据集，保持 ID 不变。</summary>
    Task RenameAsync (string id , string newName , CancellationToken ct = default);
}
