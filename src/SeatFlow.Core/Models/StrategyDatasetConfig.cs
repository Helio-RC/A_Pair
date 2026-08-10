namespace SeatFlow.Core.Models;

/// <summary>
/// 策略按数据集维度的配置，持久化到磁盘。
/// 每个策略可以有多个 StrategyDatasetConfig（对应不同的人员/会场数据集组合）。
/// 文件存储于 {AppData}/StrategyConfig/{strategyId}/{filename}.config.json。
/// </summary>
public sealed class StrategyDatasetConfig
{
    /// <summary>文件格式版本号。</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>所属策略 ID。</summary>
    public string StrategyId { get; set; } = string.Empty;

    /// <summary>引用的人员数据集 ID（可选）。</summary>
    public string? DatasetId { get; set; }

    /// <summary>引用的会场 ID（可选）。</summary>
    public string? VenueId { get; set; }

    /// <summary>人员数据哈希，与 <see cref="RosterFile.StudentsHash"/> 对应，用于检测数据变更。</summary>
    public string? StudentsHash { get; set; }

    /// <summary>会场数据哈希（VenueFile.ContentHash），用于检测数据变更。</summary>
    public string? ContentHash { get; set; }

    /// <summary>配置行列表。</summary>
    public List<StrategyConfigRow> Rows { get; set; } = [];
}

/// <summary>
/// 配置块中的单行配置记录。
/// </summary>
public sealed class StrategyConfigRow
{
    /// <summary>行序号。</summary>
    public int Index { get; set; }

    /// <summary>选中的学生 ID（仅 dataType=Student/Both 时有效）。</summary>
    public string? StudentId { get; set; }

    /// <summary>Grid 布局：行号。</summary>
    public int? SeatRow { get; set; }

    /// <summary>Grid 布局：列号。</summary>
    public int? SeatColumn { get; set; }

    /// <summary>Polar 布局：环号。</summary>
    public int? SeatRing { get; set; }

    /// <summary>Polar 布局：角度。</summary>
    public double? SeatAngle { get; set; }

    /// <summary>Freeform 布局：X 坐标。</summary>
    public double? SeatX { get; set; }

    /// <summary>Freeform 布局：Y 坐标。</summary>
    public double? SeatY { get; set; }

    /// <summary>声明的字段值（由 StrategyFieldDefinition.Name 索引）。</summary>
    public Dictionary<string , object?> Values { get; set; } = [];
}
