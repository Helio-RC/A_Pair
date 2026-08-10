using System.Text.Json;

namespace SeatFlow.Infrastructure.Tests;

public class SeatJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new SeatJsonConverter() } ,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void RoundTrip_GridSeat ()
    {
        var seat = new GridSeat { Id = "g1" , Row = 2 , Column = 3 };
        var json = JsonSerializer.Serialize<Seat>(seat , Options);
        var deserialized = JsonSerializer.Deserialize<Seat>(json , Options);
        deserialized.Should().BeOfType<GridSeat>();
        var grid = deserialized as GridSeat;
        grid!.Row.Should().Be(2);
        grid.Column.Should().Be(3);
    }

    [Fact]
    public void RoundTrip_PolarSeat ()
    {
        var seat = new PolarSeat { Id = "p1" , Radius = 1.5 , AngleDegrees = 90 };
        var json = JsonSerializer.Serialize<Seat>(seat , Options);
        var deserialized = JsonSerializer.Deserialize<Seat>(json , Options);
        deserialized.Should().BeOfType<PolarSeat>();
        var polar = deserialized as PolarSeat;
        polar!.Radius.Should().Be(1.5);
        polar.AngleDegrees.Should().Be(90);
    }

    [Fact]
    public void RoundTrip_FreeformSeat ()
    {
        var seat = new FreeformSeat { Id = "f1" , X = 3.3 , Y = 4.4 };
        var json = JsonSerializer.Serialize<Seat>(seat , Options);
        var deserialized = JsonSerializer.Deserialize<Seat>(json , Options);
        deserialized.Should().BeOfType<FreeformSeat>();
        var free = deserialized as FreeformSeat;
        free!.X.Should().Be(3.3);
        free.Y.Should().Be(4.4);
    }

    [Fact]
    public void Deserialize_UnknownType_ShouldThrow ()
    {
        const string json = "{\"Type\":\"Unknown\"}";
        Action act = () => JsonSerializer.Deserialize<Seat>(json , Options);
        act.Should().Throw<JsonException>()
           .Where(ex => ex.Message.Contains("Unsupported Seat type"));
    }
}