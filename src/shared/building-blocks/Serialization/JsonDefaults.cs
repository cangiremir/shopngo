using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShopNGo.BuildingBlocks.Serialization;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
