using SeatFlow.Application.Scripting.Lua;

namespace SeatFlow.Application.Tests.Scripting;

public class LuaScriptStrategyTests
{
    private static LuaScriptStrategy CreateStrategy (string code , int timeoutMs = 5000) =>
        new(code , "lua-test" , version: null , new LuaScriptConfiguration
        {
            StrategyName = "Lua 测试策略" ,
            TimeoutMilliseconds = timeoutMs
        });

    private static SeatingWorkspace CreateWorkspace () =>
        new([new Student { Id = "s1" , Name = "甲" }] , new Seat[] { new GridSeat { Id = "seat1" } });

    [Fact]
    public async Task ExecuteAsync_DeadLoopScript_TimesOutAndReturnsFailure ()
    {
        // 死循环脚本：超时后返回失败（宿主无法强制中断 VM，脚本在后台继续运行直至进程退出）
        var strategy = CreateStrategy("local x = 0\nwhile true do x = x + 1 end" , 300);
        var workspace = CreateWorkspace();

        var result = await strategy.ExecuteAsync(workspace , TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("超时");
    }

    [Fact]
    public async Task ExecuteAsync_RestrictedLibraryDisabled_ReturnsFailure ()
    {
        // io/os/package/debug 被禁用：访问 os 应报 Lua 错误
        var strategy = CreateStrategy("local t = os.time()" , 2000);
        var workspace = CreateWorkspace();

        var result = await strategy.ExecuteAsync(workspace , TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ImportOverridden_NoError ()
    {
        // import 被覆盖为空函数：调用任意 .NET 程序集加载不应生效也不应报错
        var strategy = CreateStrategy("import('System.IO')\nimport('System.Diagnostics')");
        var workspace = CreateWorkspace();

        var result = await strategy.ExecuteAsync(workspace , TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SimpleScript_Succeeds ()
    {
        // 正常脚本：通过受限 workspace API 分配座位（NLua 实例方法使用冒号语法）
        var strategy = CreateStrategy("workspace:AssignSeat('seat1', 's1')");
        var workspace = CreateWorkspace();

        var result = await strategy.ExecuteAsync(workspace , TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        workspace.GetAssignments().Should().ContainKey("seat1").WhoseValue.Should().Be("s1");
    }

    [Fact]
    public void Id_MatchesManifestId ()
    {
        // 缺陷 A 回归：脚本策略 ID 必须来自 manifest（构造注入），而非随机 Guid
        var strategy = CreateStrategy("-- 空脚本" , 100);

        strategy.Id.Should().Be("lua-test");
        strategy.Id.Should().NotBeEmpty();
    }
}
