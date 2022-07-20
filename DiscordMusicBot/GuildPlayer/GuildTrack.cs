using Discord.WebSocket;
using Victoria;

namespace DiscordMusicBot.GuildPlayer
{
    public class GuildTrack
    {
        public SocketGuildUser Requester { get; set; }
        public LavaTrack Track { get; set; }

        public GuildTrack(SocketGuildUser requester, LavaTrack track)
        {
            Requester = requester;
            Track = track;
        }
    }
}
