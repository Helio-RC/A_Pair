using SeatFlow.Core.Exporters;
using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Providers;
using SeatFlow.Core.Services;
using SeatFlow.Infrastructure.Migration;
using SeatFlow.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace SeatFlow.Application.Tests.Services;

public class ApplicationFacadeTests
{
    private static ApplicationFacade CreateFacade (
        out IServiceProvider serviceProvider ,
        out ISeatingSnapshotRepository snapshotRepo ,
        out ISeatingPlanExporter exporter ,
        out IPluginManager pluginManager ,
        out IPluginConfigurationService pluginConfigService ,
        out IAppSettingsRepository appSettingsRepo ,
        out IVenueRepository venueRepo ,
        out IStudentDatasetRepository datasetRepo ,
        out StrategyManifestProvider manifestProvider ,
        out StrategyConfigFileRepository strategyConfigRepo ,
        out StrategyDatasetConfigRepository datasetConfigRepo ,
        out PluginPackageConfigService pluginPackageConfigService ,
        out ISeatSetsService seatSetsService ,
        out ILogger<ApplicationFacade> logger)
    {
        serviceProvider = Substitute.For<IServiceProvider>();
        snapshotRepo = Substitute.For<ISeatingSnapshotRepository>();
        exporter = Substitute.For<ISeatingPlanExporter>();
        pluginManager = Substitute.For<IPluginManager>();
        pluginConfigService = Substitute.For<IPluginConfigurationService>();
        appSettingsRepo = Substitute.For<IAppSettingsRepository>();
        venueRepo = Substitute.For<IVenueRepository>();
        datasetRepo = Substitute.For<IStudentDatasetRepository>();
        manifestProvider = Substitute.For<StrategyManifestProvider>();
        strategyConfigRepo = Substitute.For<StrategyConfigFileRepository>("/tmp/dummy_config_dir" ,
            new FileMigrationService([]) ,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<StrategyConfigFileRepository>>());
        datasetConfigRepo = Substitute.For<StrategyDatasetConfigRepository>("/tmp/dummy_config_dir" ,
            new FileMigrationService([]) ,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<StrategyDatasetConfigRepository>>());
        pluginPackageConfigService = Substitute.For<PluginPackageConfigService>(
            pluginManager , new FileMigrationService([]) ,
            Substitute.For<ILogger<PluginPackageConfigService>>());
        seatSetsService = Substitute.For<ISeatSetsService>();
        logger = Substitute.For<ILogger<ApplicationFacade>>();

        var facade = new ApplicationFacade(
            serviceProvider ,
            snapshotRepo ,
            new[] { exporter } ,
            pluginManager ,
            pluginConfigService ,
            appSettingsRepo ,
            venueRepo ,
            datasetRepo ,
            manifestProvider ,
            strategyConfigRepo ,
            datasetConfigRepo ,
            pluginPackageConfigService ,
            seatSetsService ,
            logger);
        return facade;
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_ShouldCallExporterWithOptions ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var dcr , out var ppcs , out var sss , out var log);
        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        var options = new ExportOptions { Format = ExportFormat.Excel , Anonymize = true };

        // 设置导出器的 Format 属性
        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws , null , "test.xlsx" , options , CancellationToken.None);

        await exporter.Received(1).ExportAsync(
            Arg.Any<SeatingPlan>() ,
            "test.xlsx" ,
            Arg.Is<ExportOptions>(o => o!.Anonymize == true) ,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportSeatingPlanAsync_TeacherView_ShouldReverseRowsAndColumns ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var dcr , out var ppcs , out var sss , out var log);
        var students = new List<Student>();
        var seats = new List<Seat>
        {
            new GridSeat { Row = 1 , Column = 1 , Id = "s1" } ,
            new GridSeat { Row = 1 , Column = 2 , Id = "s2" } ,
            new GridSeat { Row = 2 , Column = 1 , Id = "s3" }
        };
        var layout = new ClassroomLayoutDefinition
        {
            Name = "测试布局" ,
            LayoutType = LayoutType.Grid ,
            Metadata = new GridLayoutMetadata { Rows = 2 , Columns = 2 , HasPodium = true } ,
            Seats = seats
        };
        var ws = new SeatingWorkspace(students , seats);
        var options = new ExportOptions { Format = ExportFormat.Excel , Perspective = LayoutPerspective.TeacherView };

        exporter.Format.Returns(ExportFormat.Excel);

        await facade.ExportSeatingPlanAsync(ws , layout , "test.xlsx" , options , CancellationToken.None);

        await exporter.Received(1).ExportLayoutAsync(
            Arg.Is<LayoutSeatingExportModel>(m =>
                // 行反转：讲台移至最后一行
                m!.Rows[m.Rows.Count - 1].Cells.Any(c => c.IsPodium) &&
                // 列镜像：讲台行内 cells 左右颠倒，讲台从中间移至另一侧
                m.Rows[m.Rows.Count - 1].Cells[0].IsPodium) ,
            "test.xlsx" ,
            Arg.Is<ExportOptions>(o => o!.Perspective == LayoutPerspective.TeacherView) ,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteCommandAsync_ShouldDelegateToHistory ()
    {
        var facade = CreateFacade(out _ , out _ , out _ ,
            out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _);
        var cmd = Substitute.For<IUndoableCommand>();
        cmd.ExecuteAsync(Arg.Any<SeatingWorkspace>() , Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var ws = new SeatingWorkspace(new List<Student>() , new List<Seat>());
        typeof(ApplicationFacade)
            .GetField("_currentWorkspace" , System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(facade , ws);

        var result = await facade.ExecuteCommandAsync(cmd , CancellationToken.None);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UndoAsync_NoWorkspace_ReturnsFalse ()
    {
        var facade = CreateFacade(out _ , out _ , out _ ,
            out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _ , out _);
        var result = await facade.UndoAsync(CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RollbackToSnapshot_ShouldApplyAssignments ()
    {
        var facade = CreateFacade(out var sp , out var snapRepo , out var exporter ,
            out var pm , out var pcs , out var appRepo , out var venueRepo , out var dr , out var mp , out var scr , out var dcr , out var ppcs , out var sss , out var log);

        // 设置会场布局，确保回滚时座位 ID 匹配
        var layout = new ClassroomLayoutDefinition
        {
            Id = "test-venue" ,
            Name = "Test Venue" ,
            LayoutType = LayoutType.Grid ,
            Seats = [new GridSeat { Id = "seat1" } , new GridSeat { Id = "seat2" }]
        };
        venueRepo.LoadAsync("test-venue" , Arg.Any<CancellationToken>()).Returns(layout);

        // 设置数据集仓库返回空（无匹配真实学生，使用存根）
        dr.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var snapshot = new SeatingSnapshot
        {
            Id = "snap-1" ,
            LayoutId = "test-venue" ,
            SeatAssignments = new Dictionary<string , string> { { "seat1" , "s1" } , { "seat2" , "s2" } }
        };
        snapRepo.LoadAsync(snapshot.Id , Arg.Any<CancellationToken>()).Returns(snapshot);

        await facade.RollbackToSnapshotAsync(snapshot.Id , CancellationToken.None);

        var workspace = await facade.GetCurrentWorkspaceAsync(TestContext.Current.CancellationToken);
        workspace.Should().NotBeNull();
        workspace!.FindSeats(s => s.Id == "seat1").First().OccupantId.Should().Be("s1");
        workspace.FindSeats(s => s.Id == "seat2").First().OccupantId.Should().Be("s2");
    }
}