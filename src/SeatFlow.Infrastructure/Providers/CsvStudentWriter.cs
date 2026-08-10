using System.Globalization;
using SeatFlow.Core.Models;
using SeatFlow.Core.Providers;
using CsvHelper;

namespace SeatFlow.Infrastructure.Providers
{
    /// <summary>
    /// CSV 格式的学生数据写入器。
    /// 第 1 行为列名，第 2 行为注释行，数据从第 3 行开始。
    /// </summary>
    public class CsvStudentWriter : IStudentWriter
    {
        public async Task WriteAsync (string path , IEnumerable<Student> students , CancellationToken cancellationToken = default)
        {
            await using var writer = new StreamWriter(path , false , new System.Text.UTF8Encoding(true));
            await using var csv = new CsvWriter(writer , CultureInfo.InvariantCulture);

            // 第 1 行：列名
            csv.WriteField("Name");
            csv.WriteField("Height");
            csv.WriteField("Gender");
            csv.WriteField("NeedsFrontRow");
            await csv.NextRecordAsync();

            // 第 2 行：注释
            csv.WriteField("必填");
            csv.WriteField("身高（厘米），如 170.5");
            csv.WriteField("男 / 女 / 其他");
            csv.WriteField("是 / 否（或 true/false）");
            await csv.NextRecordAsync();

            // 第 3 行起：数据（仅写 4 个匹配表头的字段）
            foreach (var s in students)
            {
                cancellationToken.ThrowIfCancellationRequested();
                csv.WriteField(s.Name);
                csv.WriteField(s.Height);
                csv.WriteField(s.Gender?.ToString() ?? "");
                csv.WriteField(s.NeedsFrontRow ? "true" : "false");
                await csv.NextRecordAsync();
            }
        }
    }
}
