using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordMusicBot.Attributes;
using Fergun.Interactive;
using Victoria.Responses.Search;

namespace DiscordMusicBot.Modules;

[EnabledInDm(false)]
[DefaultMemberPermissions(GuildPermission.SendMessages)]
public class AudioModule : InteractionModuleBase
{
    public AudioService Music { get; set; }
    public InteractiveService Interactive { get; set; }

    [SlashCommand("leave",
        "Get the bot to stop playing, clear requests and leave the voice channel.")]
    public async Task LeaveVCAsync()
    {
        await RespondAsync(embed: await Music.LeaveAsync(Context.Guild, Context.User));
    }

    [SlashCommand("play",
        "Resume paused track or add a track. Bot will join the voice channel if it's not already in one.")]
    public async Task PlayMusicAsync(
        [AutocompleteParameter] [Summary(description: "(Soundcloud or Youtube) URL or Youtube search terms.")]
        string query = null,
        [MusicService(SearchType.SoundCloud, SearchType.YouTube, SearchType.YouTubeMusic)]
        [Summary(description: "Service to search in 'query' (Default is Youtube). This is ignored if direct link is given.")]
        SearchType service = SearchType.YouTube)
    {
        await DeferAsync();
        var embed = await Music.PlayAsync(Context.Guild, Context.User, (ITextChannel)Context.Channel,
            query);
        if (embed != null)
            await FollowupAsync(embed: embed);
    }

    [SlashCommand("skip",
        "Skip current track.")]
    public async Task SkipMusicAsync()
    {
        await RespondAsync(embed: await Music.SkipAsync(Context.Guild, Context.User));
    }

    [SlashCommand("now",
        "Get info on the current track.")]
    public async Task NowPlayingAsync()
    {
        await DeferAsync();
        await FollowupAsync(embed: await Music.GetNowPlayingAsync(Context.Guild, Context.User));
    }

    [SlashCommand("loop",
        "Toggle looping of tracks.")]
    public async Task LoopToggleAsync()
    {
        await RespondAsync(embed: Music.ToggleLoop(Context.Guild, Context.User));
    }

    [SlashCommand("volume",
        "Set player volume.")]
    public async Task SetVolumeAsync(
        [Summary(description: "Volume to set to.")] [MinValue(0)] [MaxValue(1000)]
        int volume)
    {
        await RespondAsync(
            embed: await Music.SetVolumeAsync(Context.Guild, Context.User, Convert.ToUInt16(volume)));
    }

    [SlashCommand("pause",
        "Pause current track.")]
    public async Task PauseAsync()
    {
        await RespondAsync(embed: await Music.PauseAsync(Context.Guild, Context.User));
    }

    [SlashCommand("queue",
        "Get all requested tracks.")]
    public async Task GetQueueAsync()
    {
        await DeferAsync();
        
        var (embed, paginator) = await Music.GetQueueListAsync(Context.Guild, Context.User);

        if (paginator != null)
            await Interactive.SendPaginatorAsync(paginator, (SocketInteraction)Context.Interaction,
                TimeSpan.FromMinutes(5), InteractionResponseType.DeferredChannelMessageWithSource);
        else
            await FollowupAsync(embed: embed);
    }

    [SlashCommand("remove",
        "Remove a track from the queue.")]
    public async Task RemoveQueueAsync(
        [Summary(description: "Index of the track to remove.")]
        int index)
    {
        await RespondAsync(embed: await Music.RemoveQueueAsync(Context.Guild, Context.User, index));
    }

    [SlashCommand("stop",
        "Stop current track and clear all requested tracks.")]
    public async Task StopAsync()
    {
        await RespondAsync(embed: await Music.StopAsync(Context.Guild, Context.User));
    }

    [SlashCommand("shuffle",
        "Toggle shuffling of tracks.")]
    public async Task ShuffleToggleAsync()
    {
        await RespondAsync(embed: Music.ToggleShuffle(Context.Guild, Context.User));
    }

    [SlashCommand("seek",
        "Seek track to the specified position.")]
    public async Task SeekAsync(
        [Summary(
            description: "Time to seek to in this format: hh:mm:ss. (Example: 2 minutes 50 seconds is 2:50).")]
        TimeSpan ts)
    {
        await RespondAsync(embed: await Music.SeekAsync(Context.Guild, Context.User, ts));
    }

    [SlashCommand("speed",
        "Modify the player's playback speed.")]
    public async Task SpeedAsync(
        [Summary(description: "Playback speed to set to.")] [MinValue(0.1)] [MaxValue(5)]
        double value)
    {
        await RespondAsync(embed: await Music.SetSpeedAsync(Context.Guild, Context.User, value));
    }

    [SlashCommand("rate",
        "Modify the player's playback speed and pitch rate.")]
    public async Task RateAsync(
        [Summary(description: "Playback speed and pitch rate to set to.")] [MinValue(0.1)] [MaxValue(5)]
        double value)
    {
        await RespondAsync(embed: await Music.SetRateAsync(Context.Guild, Context.User, value));
    }

    [SlashCommand("pitch",
        "Modify the player's pitch rate.")]
    public async Task PitchAsync(
        [Summary(description: "Pitch rate to set to.")] [MinValue(0.1)] [MaxValue(5)]
        double value)
    {
        await RespondAsync(embed: await Music.SetPitchAsync(Context.Guild, Context.User, value));
    }
    
    [SlashCommand("lyrics",
        "Get the lyrics of the current track, if any.")]
    public async Task LyricsAsync(
        [Summary(description: "Name of the track's artist. Leave this and title empty to get current track.")] string artist = null,
        [Summary(description: "Name of the track. Leave this and artist empty to get current track.")] string title = null)
    {
        await DeferAsync();
        
        await FollowupAsync(embed: await Music.GetLyricsAsync(Context.Guild, Context.User, artist, title));
    }

    [SlashCommand("move",
        "Move a track to a new position in queue.")]
    public async Task LyricsAsync(
        [Summary(description: "The index of the track to move.")] [MinValue(1)] int oldIndex,
        [Summary(description: "The index to move the track to.")] [MinValue(1)] int newIndex)
    {
        await RespondAsync(embed: Music.MoveTrack(Context.Guild, Context.User, oldIndex, newIndex));
    }
}
