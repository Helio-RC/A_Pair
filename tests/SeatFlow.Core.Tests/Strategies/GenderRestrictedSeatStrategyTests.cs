using SeatFlow.Core.Enums;

namespace SeatFlow.Core.Tests.Strategies;

public class GenderRestrictedSeatStrategyTests
{
    // ═══════════════ EvaluateAsync 测试 ═══════════════

    [Fact]
    public async Task EvaluateAsync_NoRestrictions_ShouldApprove ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        // 无限制配置 → 全部放行
        var strategy = new GenderRestrictedSeatStrategy();
        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_SeatNotRestricted_ShouldApprove ()
    {
        var students = StrategyTestHelpers.CreateStudents("s1" , "s2");
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace(students , [.. seats.Cast<Seat>()]);

        // 仅限制 seat (1,2)，目标座位是 (1,1)
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[1].Id] = Gender.Female
        });

        var result = await strategy.EvaluateAsync(
            ws , students[0] , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GenderMatches_ShouldApprove ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var s2 = new Student { Id = "s2" , Name = "s2" , Gender = Gender.Female };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // 座位(1,1)限制 Male，学生 s1 是 Male → 匹配
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Male
        });

        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_GenderMismatch_WithMatchingEmpty_ShouldHandled ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var s2 = new Student { Id = "s2" , Name = "s2" , Gender = Gender.Female };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (1 , 3));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // 座位(1,1)限制 Female，座位(1,2)限制 Male（空座）
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female ,
            [seats[1].Id] = Gender.Male
        });

        // 提议 Male 学生到 Female 座位 → 应重定向到 Male 座位(1,2)
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
        result.AlreadyHandled.Should().BeTrue();
        // 验证学生已被分配到 Male 受限座位
        seats[1].OccupantId.Should().Be(s1.Id);
    }

    [Fact]
    public async Task EvaluateAsync_GenderMismatch_NoMatchingEmpty_ShouldReject ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([s1] , [.. seats.Cast<Seat>()]);

        // 座位仅限制 Female，没有 Male 受限座位可用
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female
        });

        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_GenderMismatch_RerollsExhausted_ShouldApproveWithWarning ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1] , [.. seats.Cast<Seat>()]);

        // 两个座位都限制 Female，无 Male 受限座位 — 重掷耗尽
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female ,
            [seats[1].Id] = Gender.Female
        });

        var context = new StrategyTestHelpers.TestContext(rerollCount: 9 , maxRerolls: 10);
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , context , CancellationToken.None);

        result.Approved.Should().BeTrue();
        context.Warnings.Should().HaveCount(1);
        context.Warnings[0].MessageKey.Should().Be("GenderRestrictedSeat_Forced");
    }

    [Fact]
    public async Task EvaluateAsync_FixedSeat_ShouldApprove ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        seats[0].IsFixed = true;
        var ws = new SeatingWorkspace([s1] , [.. seats.Cast<Seat>()]);

        // 固定座位限制 Female，但固定座位不干涉
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female
        });

        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_NullGender_ShouldRejectOnRestrictedSeat ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = null };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([s1] , [.. seats.Cast<Seat>()]);

        // 座位限制 Female，学生无性别 → 不匹配
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female
        });

        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        // null → Unknown，不匹配 Female → 无匹配空座 → Reject
        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_GenderOther_ShouldRejectOnRestrictedSeat ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Other };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([s1] , [.. seats.Cast<Seat>()]);

        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Male
        });

        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        // Other != Male → 无匹配空座 → Reject
        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_SetRestrictions_ShouldReplaceOld ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1] , [.. seats.Cast<Seat>()]);

        var strategy = new GenderRestrictedSeatStrategy();
        // 第一次设置：座位(1,1)限制 Female
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female
        });
        // 第二次设置：座位(1,1)限制 Male（替换）
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Male
        });

        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

        // 新限制 Male，学生 Male → 匹配
        result.Approved.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_MatchingEmptySeatOccupied_ShouldNotHandled ()
    {
        // 验证：如果匹配性别的受限座位已被占，不应选它
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var s2 = new Student { Id = "s2" , Name = "s2" , Gender = Gender.Female };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (1 , 3));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // (1,1)限制 Female, (1,2)限制 Male 但已被 s2(Female)占（实际不可能但假设占位）
        // (1,3) 无限制
        ws.TryAssignSeat(seats[1].Id , s2.Id , out _); // s2 占了 Male 座位

        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female ,
            [seats[1].Id] = Gender.Male
        });

        // 提议 Male s1 到 Female 座位(1,1)
        // (1,2)是 Male 但已被占 → 不是空座
        // 无其他 Male 空座 → Reject
        var result = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext(rerollCount: 0 , maxRerolls: 10) , CancellationToken.None);

        result.Approved.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_RedirectIsRandom_ShouldUseRandomSeat ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var s2 = new Student { Id = "s2" , Name = "s2" , Gender = Gender.Female };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2) , (1 , 3) , (1 , 4));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        // (1,1)限制 Female, (1,2)/(1,3)/(1,4)限制 Male
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Female ,
            [seats[1].Id] = Gender.Male ,
            [seats[2].Id] = Gender.Male ,
            [seats[3].Id] = Gender.Male
        });

        // 多次调用，确保重定向到匹配座位（由于随机，多次调用可能命中不同座位——只需验证总是某个Male座位）
        var redirectSeats = new HashSet<string>();
        for (int i = 0; i < 5; i++)
        {
            // 重置座位状态
            foreach (var s in seats) { s.OccupantId = null; s.IsAvailable = true; }
            var w = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

            var result = await strategy.EvaluateAsync(
                w , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);

            result.AlreadyHandled.Should().BeTrue();
            // 验证被分配到了 Male 受限座位(1,2/1,3/1,4之一)
            redirectSeats.Add(seats.First(se => se.OccupantId == s1.Id).Id);
        }
        // 至少有一次重定向到(1,2)~(1,4)之间的座位
        redirectSeats.Should().BeSubsetOf([seats[1].Id , seats[2].Id , seats[3].Id]);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleGendersInConfig_ShouldMatchCorrectly ()
    {
        var s1 = new Student { Id = "s1" , Name = "s1" , Gender = Gender.Male };
        var s2 = new Student { Id = "s2" , Name = "s2" , Gender = Gender.Female };
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1) , (1 , 2));
        var ws = new SeatingWorkspace([s1 , s2] , [.. seats.Cast<Seat>()]);

        var strategy = new GenderRestrictedSeatStrategy();
        strategy.SetRestrictions(new Dictionary<string , Gender>
        {
            [seats[0].Id] = Gender.Male ,    // Male 学生应匹配此座位
            [seats[1].Id] = Gender.Female     // Female 学生应匹配此座位
        });

        // Male 学生到 Male 座位 → Approve
        var r1 = await strategy.EvaluateAsync(
            ws , s1 , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        r1.Approved.Should().BeTrue();

        // Female 学生到 Female 座位 → Approve
        var r2 = await strategy.EvaluateAsync(
            ws , s2 , seats[1] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        r2.Approved.Should().BeTrue();
    }

    // ═══════════════ 参数校验测试 ═══════════════

    [Fact]
    public async Task EvaluateAsync_NullWorkspace_ShouldThrowArgumentNullException ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var student = new Student { Id = "s1" };

        var act = async () => await strategy.EvaluateAsync(
            null! , student , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_NullStudent_ShouldThrowArgumentNullException ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([] , [.. seats.Cast<Seat>()]);

        var act = async () => await strategy.EvaluateAsync(
            ws , null! , seats[0] , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_NullTargetSeat_ShouldThrowArgumentNullException ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        var ws = new SeatingWorkspace([new Student { Id = "s1" }] , []);
        var student = new Student { Id = "s1" };

        var act = async () => await strategy.EvaluateAsync(
            ws , student , null! , StrategyTestHelpers.CreateContext() , CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_Cancelled_ShouldThrowOperationCanceledException ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        var seats = StrategyTestHelpers.CreateGridSeats((1 , 1));
        var ws = new SeatingWorkspace([] , [.. seats.Cast<Seat>()]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await strategy.EvaluateAsync(
            ws , new Student { Id = "s1" } , seats[0] , StrategyTestHelpers.CreateContext() , cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ═══════════════ 属性测试 ═══════════════

    [Fact]
    public void Properties_ShouldHaveExpectedDefaults ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.Id.Should().Be("GenderRestrictedSeat");
        strategy.Name.Should().Be("GenderRestrictedSeat");
        strategy.DisplayName.Should().Be("性别限制座位");
        strategy.Priority.Should().Be(45);
        strategy.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Config_ShouldHaveExpectedDefaults ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.Config.SeatGenderRestrictions.Should().NotBeNull();
        strategy.Config.SeatGenderRestrictions.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfiguration_ShouldReturnValid ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        var result = strategy.ValidateConfiguration();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void GetConstrainedStudentIds_ShouldReturnEmpty ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        strategy.GetConstrainedStudentIds().Should().BeEmpty();
    }

    [Fact]
    public void SetPriorAssignedStudentIds_ShouldNotThrow ()
    {
        var strategy = new GenderRestrictedSeatStrategy();
        var act = () => strategy.SetPriorAssignedStudentIds(["id1" , "id2"]);
        act.Should().NotThrow();
    }
}
