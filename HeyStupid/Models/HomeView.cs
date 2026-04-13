namespace HeyStupid.Models
{
    using System.Text.Json.Serialization;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HomeView
    {
        List,
        Timeline
    }
}