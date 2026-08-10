namespace SeatFlow.Core.Models.SeatSets;

/// <summary>
/// 用户在导出/导入对话框中选择的数据类别。
/// </summary>
public class SeatSetsExportSelection
{
    /// <summary>是否包含应用设置。</summary>
    public bool IncludeAppSettings { get; set; } = true;

    /// <summary>是否包含会场布局。</summary>
    public bool IncludeVenues { get; set; } = true;

    /// <summary>是否包含学生数据集。</summary>
    public bool IncludeRosters { get; set; } = true;

    /// <summary>是否包含座位快照。</summary>
    public bool IncludeSnapshots { get; set; } = true;

    /// <summary>是否包含策略配置。</summary>
    public bool IncludeStrategyConfig { get; set; } = true;

    /// <summary>是否所有类别都已选中。</summary>
    public bool IsAllSelected =>
        IncludeAppSettings && IncludeVenues && IncludeRosters
        && IncludeSnapshots && IncludeStrategyConfig;

    /// <summary>是否有任何类别被选中。</summary>
    public bool IsAnySelected =>
        IncludeAppSettings || IncludeVenues || IncludeRosters
        || IncludeSnapshots || IncludeStrategyConfig;

    /// <summary>获取选中的类别名列表。</summary>
    public List<string> GetSelectedCategories ()
    {
        var list = new List<string>(5);
        if (IncludeAppSettings) list.Add(SeatSetsConstants.CategoryAppSettings);
        if (IncludeVenues) list.Add(SeatSetsConstants.CategoryVenues);
        if (IncludeRosters) list.Add(SeatSetsConstants.CategoryRosters);
        if (IncludeSnapshots) list.Add(SeatSetsConstants.CategorySnapshots);
        if (IncludeStrategyConfig) list.Add(SeatSetsConstants.CategoryStrategyConfig);
        return list;
    }
}
