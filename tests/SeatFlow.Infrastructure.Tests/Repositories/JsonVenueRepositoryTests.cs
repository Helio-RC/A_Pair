using SeatFlow.Infrastructure.Migration;
namespace SeatFlow.Infrastructure.Tests.Repositories;

public class JsonVenueRepositoryTests
{
    private static string CreateTempDirectory ()
    {
        var dir = Path.Combine(Path.GetTempPath() , Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task SaveAndLoad_GridLayout_ShouldRoundTrip ()
    {
        var dir = CreateTempDirectory();
        try
        {
            var repo = new JsonVenueRepository(dir , new FileMigrationService([]));
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Grid ,
                Name = "Test Grid" ,
                Metadata = new GridLayoutMetadata { Rows = 5 , Columns = 6 }
            };
            layout.Seats.Add(new GridSeat { Id = "g1" , Row = 1 , Column = 1 });

            await repo.SaveAsync("venue1" , layout , CancellationToken.None);
            var loaded = await repo.LoadAsync("venue1" , CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Test Grid");
            loaded.Seats.Should().HaveCount(1);
            loaded.Seats[0].Should().BeOfType<GridSeat>();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir , true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_PolarLayout_ShouldRoundTrip ()
    {
        var dir = CreateTempDirectory();
        try
        {
            var repo = new JsonVenueRepository(dir , new FileMigrationService([]));
            var layout = new ClassroomLayoutDefinition
            {
                LayoutType = LayoutType.Polar ,
                Metadata = new PolarLayoutMetadata { Rings = 2 , SeatsPerRing = 8 }
            };
            layout.Seats.Add(new PolarSeat { Id = "p1" , Radius = 1.0 , AngleDegrees = 45 });

            await repo.SaveAsync("venue2" , layout , CancellationToken.None);
            var loaded = await repo.LoadAsync("venue2" , CancellationToken.None);
            loaded.Should().NotBeNull();
            loaded!.Seats[0].Should().BeOfType<PolarSeat>();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir , true);
        }
    }

    [Fact]
    public async Task ListVenueIds_ShouldReturnAllIds ()
    {
        var dir = CreateTempDirectory();
        try
        {
            var repo = new JsonVenueRepository(dir , new FileMigrationService([]));
            await repo.SaveAsync("v1" , new ClassroomLayoutDefinition() , CancellationToken.None);
            await repo.SaveAsync("v2" , new ClassroomLayoutDefinition() , CancellationToken.None);

            var ids = (await repo.ListVenueIdsAsync(CancellationToken.None)).ToList();
            ids.Should().Contain(["v1" , "v2"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir , true);
        }
    }

    [Fact]
    public async Task LoadAsync_NonExistent_ShouldReturnNull ()
    {
        var repo = new JsonVenueRepository(CreateTempDirectory() , new FileMigrationService([]));
        var loaded = await repo.LoadAsync("missing" , CancellationToken.None);
        loaded.Should().BeNull();
    }
}