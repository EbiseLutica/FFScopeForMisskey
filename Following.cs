using System;
using Newtonsoft.Json;

public class Following : IIdentificatedModel
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("followeeId")]
    public string FolloweeId { get; set; }

    [JsonProperty("followerId")]
    public string FollowerId { get; set; }

    [JsonProperty("follower")]
    public User Follower { get; set; }

    [JsonProperty("followee")]
    public User Followee { get; set; }
}