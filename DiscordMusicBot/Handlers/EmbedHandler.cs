using Discord;
using Discord.WebSocket;
using DiscordMusicBot.Extensions;
using DiscordMusicBot.GuildPlayer;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Victoria;

namespace DiscordMusicBot.Handlers;

public static class EmbedHandler
{
    public static Embed BuildSuccessEmbed(string message, IUser user)
    {
        var name = user.GetDisplayName();
        var avatar = user.GetDisplayAvatar();

        var embed = new EmbedBuilder()
            .WithAuthor(x => x.WithIconUrl(avatar).WithUrl(avatar).WithName(name))
            .WithDescription(message)
            .WithColor(new Color(67, 181, 129));

        return embed.Build();
    }

    public static Embed BuildTrackExceptionEmbed(IVoiceChannel vc, string message)
    {
        var embed = new EmbedBuilder()
            .WithDescription(message)
            .WithColor(new Color(240, 71, 71))
            .WithFooter("Voice Channel: " + vc.Name + $" ({vc.Id})");

        return embed.Build();
    }

    public static async Task<Embed> BuildMusicAddedEmbedAsync(LavaTrack track, SocketGuildUser requester, int position,
        GuildAudio guildPlayer, LavaTrack currentTrack)
    {
        var userAvatar = requester.GetDisplayAvatar();

        var desc = !File.Exists(track.Url)
            ? $"**[{track.Title}]({track.Url} \"{track.Title}\")**\nAdded to queue position `{position}`."
            : $"**{Path.GetFileNameWithoutExtension(track.Url)}**\nAdded to queue position `{position}`.";

        var embed = new EmbedBuilder()
            .WithDescription(desc)
            .WithColor(new Color(173, 216, 230))
            .WithThumbnailUrl(await track.FetchArtworkAsync())
            .WithFooter($"Uploaded by {track.Author}", GetServiceIconForFooter(track.Url))
            .WithAuthor(x =>
                x.WithIconUrl(userAvatar).WithUrl(userAvatar)
                    .WithName(requester.GetDisplayName()));

        embed.AddField(new EmbedFieldBuilder()
        {
            Name = "Duration",
            Value = track.Duration.GetTimeFormat(),
            IsInline = true
        });

        if (!guildPlayer.Tracks.Any())
            return embed.Build();
        {
            var timeSpan = guildPlayer.Tracks.Select(x => x.Track)
                .Aggregate(TimeSpan.Zero, (current, queuedTrack) =>
                    current.Add(queuedTrack.Duration));

            timeSpan = timeSpan.Subtract(track.Duration);
            if (currentTrack != null)
            {
                var timeLeft = currentTrack.Duration.Subtract(currentTrack.Position);
                timeSpan = timeSpan.Add(timeLeft);
            }

            embed.AddField(new EmbedFieldBuilder()
            {
                Name = "Time Until Track",
                Value = timeSpan.Duration().GetTimeFormat(),
                IsInline = true
            });
        }

        return embed.Build();
    }

    public static LazyPaginator BuildQueueListPaginator(IUser user, GuildAudio guildPlayer,
        LavaTrack track)
    {
        const int maxPerPage = 5;
        var maxPages = (int)Math.Ceiling((decimal)guildPlayer.Tracks.Count / maxPerPage);

        var paginator = new LazyPaginatorBuilder()
            .WithInputType(InputType.Buttons)
            .AddUser(user)
            .WithPageFactory(page =>
            {
                var nowPlaying = guildPlayer.CurrentTrack;

                // Get page results
                var pageResults = guildPlayer.Tracks.Skip(5 * page).Take(5);
                var index = maxPerPage * page;

                // Build the description
                var desc = $"__**Now Playing**__" +
                           $"\n**[{track.Title}]({track.Url} \"{track.Title}\")**" +
                           $"\nDuration: `{track.Position.GetTimeFormat()} / {track.Duration.GetTimeFormat()}`" +
                           $" | Queued: `{nowPlaying.Requester.GetDisplayName()}`" +
                           $"\n\n__**Up Next**__";

                foreach (var track in pageResults)
                {
                    index++;
                    desc += $"\n`{index}`" +
                            $" | [{track.Track.Title}]({track.Track.Url} \"{track.Track.Title}\")" +
                            $"\n    Duration: `{track.Track.Duration.GetTimeFormat()}`" +
                            $" | Queued: `{track.Requester.GetDisplayName()}`";
                }

                return new PageBuilder()
                    .WithAuthor(user)
                    .WithDescription(desc)
                    .WithColor(new Color(114, 137, 218))
                    .WithFooter(
                        $"Page {page + 1} of {maxPages}" +
                        $" | 🔁 Loop: {(guildPlayer.IsLooping ? "✔️" : "❌")}" +
                        $" | 🔀 Shuffle: {(guildPlayer.IsShuffling ? "✔️" : "❌")}" +
                        $" | 🔊 {guildPlayer.Volume}%" +
                        $" | {guildPlayer.Tracks.Count} queued");
            })
            .WithMaxPageIndex(maxPages - 1)
            .AddOption(new Emoji("◀"), PaginatorAction.Backward)
            .AddOption(new Emoji("▶"), PaginatorAction.Forward)
            .AddOption(new Emoji("🔢"), PaginatorAction.Jump)
            .AddOption(new Emoji("🛑"), PaginatorAction.Exit)
            .WithCacheLoadedPages(false)
            .WithActionOnCancellation(ActionOnStop.DeleteMessage)
            .WithActionOnTimeout(ActionOnStop.DeleteMessage)
            .WithFooter(PaginatorFooter.None)
            .Build();

        return paginator;
    }

    public static Embed BuildPlaylistAddedEmbed(IEnumerable<LavaTrack> tracks, string playlistName,
        SocketGuildUser requester)
    {
        var userAvatar = requester.GetAvatarUrl(size: 512) ?? requester.GetDefaultAvatarUrl();
        var desc = $"`{tracks.Count()}` tracks from `{playlistName}` added to queue";

        TimeSpan totalDuration = new();
        totalDuration = tracks.Aggregate(totalDuration, (current, track) => current + track.Duration);

        var embed = new EmbedBuilder()
            .WithDescription(desc)
            .WithColor(new Color(173, 216, 230))
            .WithFooter($"Total Duration: {totalDuration.GetTimeFormat()}")
            .WithAuthor(x =>
                x.WithIconUrl(userAvatar).WithUrl(userAvatar)
                    .WithName(requester.Nickname ?? requester.Username + "#" + requester.DiscriminatorValue));

        return embed.Build();
    }

    public static async Task<Embed> BuildNowPlayingEmbedAsync(LavaTrack track, GuildAudio guildPlayer)
    {
        var userAvatar = guildPlayer.CurrentTrack.Requester.GetAvatarUrl(size: 512) ??
                         guildPlayer.CurrentTrack.Requester.GetDefaultAvatarUrl();

        var desc = !File.Exists(track.Url)
            ? $"**Now Playing**\n**[{track.Title}]({track.Url} \"{track.Title}\")**"
            : $"**Now Playing**\n**{Path.GetFileNameWithoutExtension(track.Url)}**";
        
        var iconUrl = "https://upload.wikimedia.org/wikipedia/commons/thumb/0/09/YouTube_full-color_icon_%282017%29.svg/2560px-YouTube_full-color_icon_%282017%29.svg.png";
        if (track.Url.Contains("soundcloud", StringComparison.OrdinalIgnoreCase))
            iconUrl =
                "https://m.sndcdn.com/_next/static/images/apple-touch-icon-180-893d0d532e8fbba714cceb8d9eae9567.png";

        var embed = new EmbedBuilder()
            .WithDescription(desc)
            .WithColor(new Color(67, 181, 129))
            .WithThumbnailUrl(await track.FetchArtworkAsync())
            .WithFooter(
                $"Uploaded by {track.Author} | 🔁 Loop: {(guildPlayer.IsLooping ? "✔️" : "❌")} " +
                $"| 🔀 Shuffle: {(guildPlayer.IsShuffling ? "✔️" : "❌")} | 🔊 {guildPlayer.Volume}%",
                iconUrl)
            .WithAuthor(x => x.WithIconUrl(userAvatar).WithUrl(userAvatar).WithName(
                guildPlayer.CurrentTrack.Requester.Nickname ?? guildPlayer.CurrentTrack.Requester.Username + "#" +
                guildPlayer.CurrentTrack.Requester.DiscriminatorValue));

        embed.AddField(new EmbedFieldBuilder()
        {
            Name = "Duration",
            Value = track.Position.Seconds == 0
                ? $"{track.Duration.GetTimeFormat()}"
                : $"{track.Position.GetTimeFormat()} / {track.Duration.GetTimeFormat()}",
            IsInline = true
        });

        if (guildPlayer.Tracks.Any())
            embed.AddField(new EmbedFieldBuilder()
            {
                Name = "Next in Queue",
                Value = guildPlayer.Tracks.FirstOrDefault()?.Track.Title,
                IsInline = true
            });

        return embed.Build();
    }

    public static async Task<Embed> BuildRemovedQueueEmbedAsync(GuildTrack track, IUser user)
    {
        var userAvatar = user.GetDisplayAvatar();
        var desc =
            $"**Track removed from queue**\n**[{track.Track.Title}]({track.Track.Url} \"{track.Track.Title}\")**";

        var embed = new EmbedBuilder()
            .WithDescription(desc)
            .WithColor(new Color(240, 71, 71))
            .WithThumbnailUrl(await track.Track.FetchArtworkAsync())
            .WithAuthor(x =>
                x.WithIconUrl(userAvatar).WithUrl(userAvatar)
                    .WithName(user.GetDisplayName()))
            .WithFooter(x =>
                x.Text =
                    $"Track queued by {track.Requester.GetDisplayName()}");

        return embed.Build();
    }
    
    public static async Task<Embed> BuildTrackLyricsEmbedAsync(string lyrics, string title)
    {
        var embed = new EmbedBuilder()
            .WithDescription(lyrics)
            .WithColor(new Color(0, 255, 0))
            .WithTitle(title);

        return await Task.FromResult(embed.Build());
    }
    
    public static async Task<Embed> BuildTrackLyricsEmbedAsync(GuildTrack track, string lyrics, string title)
    {
        var embed = new EmbedBuilder()
            .WithDescription(lyrics)
            .WithColor(new Color(0, 255, 0))
            .WithTitle(title)
            .WithFooter(x =>
                x.Text =
                    $"Track queued by {track.Requester.GetDisplayName()}");

        return await Task.FromResult(embed.Build());
    }

    public static Embed BuildFailedEmbed(string error, IUser user)
    {
        var avatarUrl = user.GetDisplayAvatar();

        var embed = new EmbedBuilder()
            .WithDescription(error)
            .WithColor(new Color(240, 71, 71))
            .WithAuthor(x => x.WithIconUrl(avatarUrl).WithUrl(avatarUrl).WithName(user.GetDisplayName()));

        return embed.Build();
    }

    public static Embed BuildFailedEmbed(Exception ex, IUser user)
        => BuildFailedEmbed(ex.Message, user);

    private static string GetServiceIconForFooter(string url)
    {
        // Because track.Source returns null
        switch (url.ToLower())
        {
            case { } sc when sc.Contains("soundcloud"):
                return "https://m.sndcdn.com/_next/static/images/apple-touch-icon-180-893d0d532e8fbba714cceb8d9eae9567.png";
            case { } yt when yt.Contains("youtube"):
            case { } yt2 when yt2.Contains("yt"):
            case { } yt3 when yt3.Contains("youtu.be"):
                return "https://upload.wikimedia.org/wikipedia/commons/thumb/0/09/YouTube_full-color_icon_%282017%29.svg/2560px-YouTube_full-color_icon_%282017%29.svg.png";
            default:
                return null;
        }
    }
}
