namespace SeatFlow.Core.Models;

/// <summary>
/// 视角枚举，控制导出时的行排列顺序。
/// </summary>
public enum LayoutPerspective
{
    /// <summary>学生视角：讲台在上方，第一排（最近讲台）在顶部。</summary>
    StudentView = 0,

    /// <summary>教师视角：讲台在下方，最后一排（最远讲台）在顶部。</summary>
    TeacherView = 1
}
