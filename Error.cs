using Newtonsoft.Json;

public class Error
{
    [JsonProperty("message")]
    public string Message { get; set; }
    [JsonProperty("code")]
    public string Code { get; set; }
    [JsonProperty("id")]
    public string Id { get; set; }
    [JsonProperty("kind")]
    public string Kind { get; set; }
}
