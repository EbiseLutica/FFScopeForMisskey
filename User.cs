using System.Collections.Generic;

using Newtonsoft.Json;

public class User : IIdentificatedModel
{
    [JsonProperty("host")]
    public string Host { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("notesCount")]
    public long NotesCount { get; set; }

    [JsonProperty("pinnedNotes")]
    public IEnumerable<Note> PinnedNotes { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; }

    [JsonProperty("isFollowing")]
    public bool IsFollowing { get; set; }

    [JsonProperty("isFollowed")]
    public bool IsFollowed { get; set; }
}