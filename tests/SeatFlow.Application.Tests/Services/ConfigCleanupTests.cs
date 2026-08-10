namespace SeatFlow.Application.Tests.Services;

public class ConfigCleanupTests
{
    // ── CleanInvalidSeatRows ──

    [Fact]
    public void CleanInvalidSeatRows_AllValid_NoChange ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            LayoutType = LayoutType.Grid ,
            Seats = [new GridSeat { Id = "s1" , Row = 1 , Column = 1 } , new GridSeat { Id = "s2" , Row = 2 , Column = 3 }]
        };
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { SeatRow = 1 , SeatColumn = 1 } ,
                new() { SeatRow = 2 , SeatColumn = 3 }
            ]
        };

        bool changed = ApplicationFacade.CleanInvalidSeatRows(config , layout);

        changed.Should().BeFalse();
        config.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void CleanInvalidSeatRows_SomeOOB_RowsRemoved ()
    {
        var layout = new ClassroomLayoutDefinition
        {
            LayoutType = LayoutType.Grid ,
            Seats = [new GridSeat { Id = "s1" , Row = 1 , Column = 1 }]
        };
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { SeatRow = 1 , SeatColumn = 1 } ,  // valid
                new() { SeatRow = 99 , SeatColumn = 99 }   // OOB
            ]
        };

        bool changed = ApplicationFacade.CleanInvalidSeatRows(config , layout);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(1);
        config.Rows[0].SeatRow.Should().Be(1);
    }

    [Fact]
    public void CleanInvalidSeatRows_NullLayout_RemovesAllPositionedRows ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { SeatRow = 1 , SeatColumn = 1 } ,
                new() { SeatRow = 2 , SeatColumn = 3 } ,
                new() { StudentId = "s1" }  // 无座位位置的纯学生行，应保留
            ]
        };

        bool changed = ApplicationFacade.CleanInvalidSeatRows(config , null);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(1);
        config.Rows[0].StudentId.Should().Be("s1");
    }

    [Fact]
    public void CleanInvalidSeatRows_EmptyConfig_NoChange ()
    {
        var layout = new ClassroomLayoutDefinition { LayoutType = LayoutType.Grid , Seats = [] };
        var config = new StrategyDatasetConfig { Rows = [] };

        bool changed = ApplicationFacade.CleanInvalidSeatRows(config , layout);

        changed.Should().BeFalse();
    }

    // ── CleanFixedSeatDeletedStudents ──

    [Fact]
    public void CleanFixedSeatDeletedStudents_AllValid_NoChange ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { StudentId = "s1" } ,
                new() { StudentId = "s2" }
            ]
        };
        var validIds = new HashSet<string> { "s1" , "s2" , "s3" };

        bool changed = ApplicationFacade.CleanFixedSeatDeletedStudents(config , validIds);

        changed.Should().BeFalse();
        config.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void CleanFixedSeatDeletedStudents_SomeGone_RowsRemoved ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { StudentId = "s1" } ,
                new() { StudentId = "s2" }   // deleted
            ]
        };
        var validIds = new HashSet<string> { "s1" , "s3" };

        bool changed = ApplicationFacade.CleanFixedSeatDeletedStudents(config , validIds);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(1);
        config.Rows[0].StudentId.Should().Be("s1");
    }

    [Fact]
    public void CleanFixedSeatDeletedStudents_EmptyStudentId_Kept ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new() { StudentId = "" } ,        // empty student ID = just mark seat fixed
                new() { StudentId = "s2" }        // deleted
            ]
        };
        var validIds = new HashSet<string> { "s1" };

        bool changed = ApplicationFacade.CleanFixedSeatDeletedStudents(config , validIds);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(1);
        config.Rows[0].StudentId.Should().Be("");
    }

    [Fact]
    public void CleanFixedSeatDeletedStudents_EmptyValidSet_RemovesAll ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows = [new() { StudentId = "s1" } , new() { StudentId = "s2" }]
        };

        bool changed = ApplicationFacade.CleanFixedSeatDeletedStudents(config , []);

        changed.Should().BeTrue();
        config.Rows.Should().BeEmpty();
    }

    // ── CleanDeskMateDeletedStudents ──

    [Fact]
    public void CleanDeskMateDeletedStudents_AllValid_NoChange ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new()
                {
                    StudentId = "s1" ,
                    Values = new Dictionary<string , object?> { ["student1"] = "s2" , ["student2"] = "s3" }
                }
            ]
        };
        var validIds = new HashSet<string> { "s1" , "s2" , "s3" };

        bool changed = ApplicationFacade.CleanDeskMateDeletedStudents(config , validIds);

        changed.Should().BeFalse();
        config.Rows.Should().HaveCount(1);
    }

    [Fact]
    public void CleanDeskMateDeletedStudents_OneGone_GroupRepacked ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new()
                {
                    StudentId = "s1" ,
                    SeatRow = 2 , SeatColumn = 3 ,
                    Values = new Dictionary<string , object?> { ["student1"] = "s2" , ["student2"] = "s3" }
                }
            ]
        };
        var validIds = new HashSet<string> { "s1" , "s3" };  // s2 deleted

        bool changed = ApplicationFacade.CleanDeskMateDeletedStudents(config , validIds);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(1);
        config.Rows[0].StudentId.Should().Be("s1");
        config.Rows[0].SeatRow.Should().Be(2);
        config.Rows[0].SeatColumn.Should().Be(3);
        config.Rows[0].Values!["student1"].Should().Be("s3");
        config.Rows[0].Values!.ContainsKey("student2").Should().BeFalse();
    }

    [Fact]
    public void CleanDeskMateDeletedStudents_TwoGone_OneLeft_RowRemoved ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new()
                {
                    StudentId = "s1" ,
                    Values = new Dictionary<string , object?> { ["student1"] = "s2" }
                }
            ]
        };
        var validIds = new HashSet<string> { "s1" };  // only s1 remains, s2 deleted

        bool changed = ApplicationFacade.CleanDeskMateDeletedStudents(config , validIds);

        changed.Should().BeTrue();
        config.Rows.Should().BeEmpty();
    }

    [Fact]
    public void CleanDeskMateDeletedStudents_SeatPositionPreserved ()
    {
        var config = new StrategyDatasetConfig
        {
            Rows =
            [
                new()
                {
                    StudentId = "s1" ,
                    SeatRow = 5 , SeatColumn = 7 , SeatRing = 2 , SeatAngle = 90 , SeatX = 10.5 , SeatY = 20.5 ,
                    Values = new Dictionary<string , object?> { ["student1"] = "s2" , ["student2"] = "s3" }
                }
            ]
        };
        var validIds = new HashSet<string> { "s1" , "s2" };  // s3 deleted

        bool changed = ApplicationFacade.CleanDeskMateDeletedStudents(config , validIds);

        changed.Should().BeTrue();
        config.Rows.Should().HaveCount(1);
        config.Rows[0].SeatRow.Should().Be(5);
        config.Rows[0].SeatColumn.Should().Be(7);
        config.Rows[0].SeatRing.Should().Be(2);
        config.Rows[0].SeatAngle.Should().Be(90);
        config.Rows[0].SeatX.Should().Be(10.5);
        config.Rows[0].SeatY.Should().Be(20.5);
        config.Rows[0].Values!["student1"].Should().Be("s2");
    }
}
