namespace SeatFlow.Core.Models;

/// <summary>
/// 结构化座位安排导出模型，包含布局结构和学生姓名信息。
/// </summary>
public class LayoutSeatingExportModel
{
    public string LayoutName { get; set; } = string.Empty;
    public LayoutType LayoutType { get; set; }
    public List<ExportRow> Rows { get; set; } = [];

    public static LayoutSeatingExportModel FromLayout (
        ClassroomLayoutDefinition layout ,
        Dictionary<string , string> assignments ,
        Dictionary<string , string> studentNames)
    {
        return layout.LayoutType switch
        {
            LayoutType.Grid => BuildGrid(layout , assignments , studentNames),
            LayoutType.Polar => BuildPolar(layout , assignments , studentNames),
            _ => BuildFreeform(layout , assignments , studentNames)
        };
    }

    private static LayoutSeatingExportModel BuildGrid (
        ClassroomLayoutDefinition layout ,
        Dictionary<string , string> assignments ,
        Dictionary<string , string> studentNames)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name , LayoutType = LayoutType.Grid };
        if (layout.Metadata is not GridLayoutMetadata meta) return model;

        var seatMap = layout.Seats.OfType<GridSeat>()
            .ToDictionary(s => (s.Row , s.Column) , s => s);
        var aisleColSet = new HashSet<int>(meta.AisleAfterColumns);
        var aisleRowSet = new HashSet<int>(meta.AisleAfterRows);
        var emptyPos = new HashSet<(int , int)>(
            meta.EmptyPositions.Select(p => (p.Row , p.Column)));

        // 构建列计划: 每个槽位是 (列号, 是否过道)
        var colPlan = new List<(int? Col , bool IsAisle)>();
        for (int c = 1; c <= meta.Columns; c++)
        {
            colPlan.Add((c , false));
            if (aisleColSet.Contains(c))
                colPlan.Add((null , true));
        }

        // 讲台行（居中）
        if (meta.HasPodium)
        {
            var podiumRow = new ExportRow();
            int mid = colPlan.Count / 2;
            for (int i = 0; i < colPlan.Count; i++)
            {
                if (i == mid)
                    podiumRow.Cells.Add(new ExportCell { IsPodium = true , Text = "讲台" });
                else
                    podiumRow.Cells.Add(new ExportCell { Text = "" });
            }
            model.Rows.Add(podiumRow);
        }

        int maxRows = meta.ColumnRowCounts is { Count: > 0 }
            ? meta.ColumnRowCounts.Max()
            : meta.Rows;

        for (int r = 1; r <= maxRows; r++)
        {
            var row = new ExportRow();
            foreach (var (col , isAisle) in colPlan)
            {
                if (isAisle)
                {
                    row.Cells.Add(new ExportCell { IsAisle = true , Text = "" });
                }
                else if (col.HasValue && emptyPos.Contains((r , col.Value)))
                {
                    row.Cells.Add(new ExportCell { Text = "" });
                }
                else if (col.HasValue && seatMap.TryGetValue((r , col.Value) , out var seat))
                {
                    string? studentName = null;
                    bool isUnassigned = true;
                    if (assignments.TryGetValue(seat.Id , out var sid) &&
                        studentNames.TryGetValue(sid , out var name))
                    {
                        studentName = name;
                        isUnassigned = false;
                    }

                    row.Cells.Add(new ExportCell
                    {
                        IsSeat = true ,
                        IsUnassigned = isUnassigned ,
                        Text = studentName ?? "未分配"
                    });
                }
                else
                {
                    row.Cells.Add(new ExportCell { Text = "" });
                }
            }
            model.Rows.Add(row);

            // 过道行
            if (aisleRowSet.Contains(r))
            {
                var aisleRow = new ExportRow();
                for (int i = 0; i < colPlan.Count; i++)
                    aisleRow.Cells.Add(new ExportCell { IsAisle = true , Text = "" });
                model.Rows.Add(aisleRow);
            }
        }

        // 门（从 Obstacles 提取）
        foreach (var obs in layout.Obstacles.Where(o => o.Type == "Door"))
        {
            var doorRow = new ExportRow();
            int doorCol = colPlan.Count / 2;
            for (int i = 0; i < colPlan.Count; i++)
                doorRow.Cells.Add(new ExportCell { Text = i == doorCol ? $"[门] ({obs.X:F0}, {obs.Y:F0})" : "" });
            model.Rows.Add(doorRow);
        }

        return model;
    }

    private static LayoutSeatingExportModel BuildPolar (
        ClassroomLayoutDefinition layout ,
        Dictionary<string , string> assignments ,
        Dictionary<string , string> studentNames)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name , LayoutType = LayoutType.Polar };
        if (layout.Metadata is not PolarLayoutMetadata meta) return model;

        var polarSeats = layout.Seats.OfType<PolarSeat>().ToList();
        var rings = polarSeats.GroupBy(s => s.Ring).OrderBy(g => g.Key).ToList();

        if (rings.Count == 0) return model;

        int maxRingSeats = Math.Min(rings.Max(g => g.Count()) , 30);

        // 讲台行（居中）
        if (meta.HasPodium && meta.PodiumRadius > 0)
        {
            var podiumRow = new ExportRow();
            int padBefore = (maxRingSeats - 1) / 2;
            for (int i = 0; i < padBefore; i++)
                podiumRow.Cells.Add(new ExportCell { Text = "" });
            podiumRow.Cells.Add(new ExportCell { IsPodium = true , Text = "讲台" });
            while (podiumRow.Cells.Count < maxRingSeats)
                podiumRow.Cells.Add(new ExportCell { Text = "" });
            model.Rows.Add(podiumRow);
        }

        // 从内环到外环，等腰梯形排列
        foreach (var ringGroup in rings)
        {
            var row = new ExportRow();
            var seatsInRing = ringGroup.OrderBy(s => s.AngleDegrees).ToList();
            int ringSeatCount = Math.Min(seatsInRing.Count , maxRingSeats);

            int padding = (maxRingSeats - ringSeatCount) / 2;
            for (int i = 0; i < padding; i++)
                row.Cells.Add(new ExportCell { Text = "" });

            foreach (var seat in seatsInRing.Take(maxRingSeats))
            {
                string? studentName = null;
                bool isUnassigned = true;
                if (assignments.TryGetValue(seat.Id , out var sid) &&
                    studentNames.TryGetValue(sid , out var name))
                {
                    studentName = name;
                    isUnassigned = false;
                }

                row.Cells.Add(new ExportCell
                {
                    IsSeat = true ,
                    IsUnassigned = isUnassigned ,
                    Text = studentName ?? "未分配"
                });
            }

            while (row.Cells.Count < maxRingSeats)
                row.Cells.Add(new ExportCell { Text = "" });

            model.Rows.Add(row);
        }

        // 门（从 Obstacles 提取）
        foreach (var obs in layout.Obstacles.Where(o => o.Type == "Door"))
        {
            var doorRow = new ExportRow();
            int padBefore = maxRingSeats / 2;
            for (int i = 0; i < padBefore; i++)
                doorRow.Cells.Add(new ExportCell { Text = "" });
            doorRow.Cells.Add(new ExportCell { Text = $"[门] ({obs.X:F0}, {obs.Y:F0})" });
            while (doorRow.Cells.Count < maxRingSeats)
                doorRow.Cells.Add(new ExportCell { Text = "" });
            model.Rows.Add(doorRow);
        }

        return model;
    }

    private static LayoutSeatingExportModel BuildFreeform (
        ClassroomLayoutDefinition layout ,
        Dictionary<string , string> assignments ,
        Dictionary<string , string> studentNames)
    {
        var model = new LayoutSeatingExportModel { LayoutName = layout.Name , LayoutType = LayoutType.Freeform };
        var freeSeats = layout.Seats.OfType<FreeformSeat>().ToList();

        int idx = 0;
        foreach (var seat in freeSeats)
        {
            idx++;
            string? studentName = null;
            bool isUnassigned = true;
            if (assignments.TryGetValue(seat.Id , out var sid) &&
                studentNames.TryGetValue(sid , out var name))
            {
                studentName = name;
                isUnassigned = false;
            }

            var row = new ExportRow();
            row.Cells.Add(new ExportCell { IsSeat = true , IsUnassigned = isUnassigned , Text = studentName ?? "未分配" });
            model.Rows.Add(row);
        }

        foreach (var obs in layout.Obstacles)
        {
            var row = new ExportRow();
            row.Cells.Add(new ExportCell { Text = $"[{obs.Type}] ({obs.X:F0}, {obs.Y:F0})" });
            model.Rows.Add(row);
        }

        return model;
    }
}

public class ExportRow
{
    public List<ExportCell> Cells { get; set; } = [];
}

public class ExportCell
{
    public string Text { get; set; } = "";
    public bool IsSeat { get; set; }
    public bool IsAisle { get; set; }
    public bool IsPodium { get; set; }
    public bool IsUnassigned { get; set; }
}
