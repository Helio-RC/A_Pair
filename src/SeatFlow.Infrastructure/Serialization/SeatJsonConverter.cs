using System.Text.Json;
using System.Text.Json.Serialization;
using SeatFlow.Core.Models;

namespace SeatFlow.Infrastructure.Serialization
{
    public class SeatJsonConverter : JsonConverter<Seat>
    {
        public override Seat? Read (ref Utf8JsonReader reader , Type typeToConvert , JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type" , out var typeProp))
                throw new JsonException("Seat must contain 'Type' property");

            string typeString;
            if (typeProp.ValueKind == JsonValueKind.Number)
            {
                int typeInt = typeProp.GetInt32();
                typeString = ((SeatType)typeInt).ToString();
            }
            else if (typeProp.ValueKind == JsonValueKind.String)
            {
                typeString = typeProp.GetString()!;
            }
            else
            {
                throw new JsonException($"Invalid Type value kind: {typeProp.ValueKind}");
            }

            return typeString switch
            {
                "Grid" => Deserialize<GridSeat>(root , options),
                "Polar" => Deserialize<PolarSeat>(root , options),
                "Freeform" => Deserialize<FreeformSeat>(root , options),
                _ => throw new JsonException($"Unsupported Seat type: {typeString}")
            };
        }

        public override void Write (Utf8JsonWriter writer , Seat value , JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteString("Type" , value.Type.ToString());

            // 使用 SerializeToElement 直接序列化为 JsonElement，避免字符串往返
            var tempElement = JsonSerializer.SerializeToElement(value , value.GetType() , options);
            foreach (var prop in tempElement.EnumerateObject())
            {
                if (prop.Name == "Type") continue;
                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        private static T? Deserialize<T> (JsonElement element , JsonSerializerOptions options) where T : Seat
        {
            var json = element.GetRawText();
            return JsonSerializer.Deserialize<T>(json , options);
        }
    }
}