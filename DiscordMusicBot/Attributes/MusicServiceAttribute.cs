using Victoria.Responses.Search;

namespace DiscordMusicBot.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class MusicServiceAttribute : Attribute
{
    public IReadOnlyCollection<SearchType> MusicServices { get; }
    
    public MusicServiceAttribute(params SearchType[] musicServices)
        => MusicServices = musicServices != null
            ? musicServices.ToList()
            : throw new ArgumentNullException(nameof(musicServices));
}