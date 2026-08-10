// 空座顺序分配脚本策略（C# / Roslyn scripting）
// 通过全局对象 Workspace（IPluginWorkspace）访问受限 API
var assigned = Workspace.GetAssignments().Values.ToHashSet();
var students = Workspace.Students
    .Where(s => !assigned.Contains(s.Id))
    .OrderBy(s => s.Id)
    .ToList();
var seats = Workspace.GetEmptySeats()
    .OrderBy(s => s.Id)
    .ToList();

var count = Math.Min(students.Count, seats.Count);
for (int i = 0; i < count; i++)
{
    Workspace.TryAssignSeat(seats[i].Id, students[i].Id, out _);
}
