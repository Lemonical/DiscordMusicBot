#pragma warning disable CS8619
#pragma warning disable CS8625
using Discord;
using Discord.WebSocket;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DiscordMusicBot.Extensions;
using DiscordMusicBot.GuildPlayer;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Victoria;
using Victoria.Enums;
using Victoria.Filters;
using Victoria.Resolvers;
using Victoria.Responses.Search;

namespace DiscordMusicBot.Services;

public class AudioService
{
    private readonly LavaNode _lavaNode;

    public AudioService(LavaNode lavaNode)
        => _lavaNode = lavaNode;

    private readonly ConcurrentDictionary<ulong, GuildAudio> _guildPlayer = new();

    private readonly Regex _videoTitleRegex =
        new Regex(
            @"((?<artist>[\w\d ']+)\s-\s)?((?<title>[\w\d ']+)(?:\s\S+)?(\([\w\d ]+\))?)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    #region Commands

    public async Task<Embed> JoinAsync(IGuild guild, IUser user, ITextChannel textChannel)
    {
        // Bot is already in voice
        if (_lavaNode.HasPlayer(guild))
            return EmbedHandler.BuildFailedEmbed(
                $"\\❌ I'm already in **{_lavaNode.GetPlayer(guild).VoiceChannel.Name}**", user);

        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Check if bot can connect and speak 
        if (!((SocketGuild)guild).CurrentUser.GuildPermissions.Connect ||
            !((SocketGuild)guild).CurrentUser.GuildPermissions.Speak)
            return EmbedHandler.BuildFailedEmbed(
                "\\❌ Bot does not have the permission to connect and/or speak in that voice channel!", user);

        // If user is not connected to any voice channel
        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must be connected to a voice channel!", user);

        try
        {
            // Connect to voice channel
            var botPlayer = await _lavaNode.JoinAsync(voiceState.VoiceChannel, textChannel);

            // Create guildaudio class
            if (_guildPlayer.TryAdd(guild.Id, new(botPlayer, LeaveAsync)))
            {
                return null;
            }

            // Remove the key from dictionary
            _guildPlayer.Remove(guild.Id, out _);

            // Disconnect from vc
            await _lavaNode.LeaveAsync(voiceState.VoiceChannel);

            // Return error message
            return EmbedHandler.BuildFailedEmbed("\\❌ Could not create music player, please try again.", user);
        }

        catch (Exception ex)
        {
            return EmbedHandler.BuildFailedEmbed(ex, user);
        }
    }

    public async Task<Embed> LeaveAsync(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Get lavanode if bot is in voice, otherwise return OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != botPlayer.VoiceChannel ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildAudio))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        var vc = botPlayer.VoiceChannel;

        try
        {
            // Clear queue and stop music
            await StopAsync(guild, user);

            // Dispose
            guildAudio.Dispose();

            // Disconnect from voice channel
            await LeaveAsync(botPlayer);

            return EmbedHandler.BuildSuccessEmbed($"\\✔️Left {vc.Name}", user);
        }

        catch (Exception ex)
        {
            return EmbedHandler.BuildFailedEmbed(ex, user);
        }
    }

    public async Task<Embed> StopAsync(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Get lavanode if bot is in voice, otherwise return OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != botPlayer.VoiceChannel ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildAudio))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        try
        {
            // Clear everything
            guildAudio.CurrentTrack = null;
            guildAudio.Tracks.Clear();
            guildAudio.StartInactivityTimer();

            if (botPlayer.Track != null)
                await botPlayer.StopAsync();

            return EmbedHandler.BuildSuccessEmbed("\\✔️Track stopped and queue is cleared.", user);
        }

        catch (Exception ex)
        {
            return EmbedHandler.BuildFailedEmbed(ex, user);
        }
    }

    public async Task<SearchResponse> SearchAsync(string query, SearchType service)
    {
        return await _lavaNode.SearchAsync(service, query);
    }

    public async Task<Embed> PlayAsync(IGuild guild, IUser user, ITextChannel textChannel, string query)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;
        var joined = false;

        // Join if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer))
        {
            var embed = await JoinAsync(guild, user, textChannel);

            if (embed != null)
                return embed;

            joined = true;
            botPlayer = _lavaNode.GetPlayer(guild);
        }

        if (voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel)
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must join voice channel to use this command!", user);

        if (!_guildPlayer.TryGetValue(guild.Id, out var guildPlayer) || guildPlayer == null)
            return EmbedHandler.BuildFailedEmbed($"\\❌ Could not get music player!.", user);

        if (botPlayer.Track != null && botPlayer.PlayerState == PlayerState.Paused &&
            string.IsNullOrWhiteSpace(query))
            return await ResumeAsync(guild, user);

        try
        {
            // Get the requester
            var requester = user as SocketGuildUser;

            LavaTrack track;

            var search = Uri.IsWellFormedUriString(query, UriKind.Absolute)
                ? await _lavaNode.SearchAsync(SearchType.Direct, query)
                : await _lavaNode.SearchYouTubeAsync(query);

            // No results
            if (search.Status == SearchStatus.NoMatches)
                return EmbedHandler.BuildFailedEmbed($"\\❌ Could not find `{query}`.", user);

            // Result is playlist
            if (!EqualityComparer<SearchPlaylist>.Default.Equals(search.Playlist, default))
            {
                // Add the selected track
                track = search.Tracks.ElementAtOrDefault(search.Playlist.SelectedTrack);

                // Enqueue
                guildPlayer.Enqueue(new(requester, track));

                // Add the rest
                foreach (var playlistTrack in search.Tracks.Where(x => x != track))
                {
                    guildPlayer.Enqueue(new(requester, playlistTrack));
                }

                // If no track is playing
                if (botPlayer.Track != null)
                    return EmbedHandler.BuildPlaylistAddedEmbed(search.Tracks, search.Playlist.Name, requester);
                {
                    // Remove the added first track
                    guildPlayer.Dequeue();

                    // Play the track
                    await botPlayer.PlayAsync(track);

                    return EmbedHandler.BuildPlaylistAddedEmbed
                        (search.Tracks.Where(x => x != track), search.Playlist.Name, requester);
                }
            }

            // Get first track
            track = search.Tracks.FirstOrDefault();

            // If a track is currently playing
            if (botPlayer.Track != null)
            {
                // Add the track to queue
                guildPlayer.Enqueue(new(requester, track));
                return await EmbedHandler.BuildMusicAddedEmbedAsync
                    (track, requester, guildPlayer.Tracks.Count, guildPlayer, botPlayer.Track);
            }

            // Play the track
            guildPlayer.CurrentTrack = new(requester, track);
            await botPlayer.PlayAsync(track);

            return joined
                ? EmbedHandler.BuildSuccessEmbed($"\\✔️Joined {botPlayer.VoiceChannel.Name}.", user)
                : EmbedHandler.BuildSuccessEmbed($"\\✔️Nothing is queued, playing track now.", user);
        }

        catch (Exception ex)
        {
            return EmbedHandler.BuildFailedEmbed(ex, user);
        }
    }

    public async Task<Embed> SetSpeedAsync(IGuild guild, IUser user, double value)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        // Apply speed
        await botPlayer.ApplyFilterAsync(new TimescaleFilter()
        {
            Speed = value,
            Rate = 1,
            Pitch = 1
        }, guildPlayer.Volume / 100);

        return EmbedHandler.BuildSuccessEmbed($"🕒 Speed set to **{value}**.", user);
    }

    public async Task<Embed> SetPitchAsync(IGuild guild, IUser user, double value)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        // Apply pitch
        await botPlayer.ApplyFilterAsync(new TimescaleFilter()
        {
            Speed = 1,
            Rate = 1,
            Pitch = value
        }, guildPlayer.Volume / 100);

        return EmbedHandler.BuildSuccessEmbed($"🕒 Pitch set to **{value}**.", user);
    }

    public async Task<Embed> SetRateAsync(IGuild guild, IUser user, double value)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        // Apply rate
        await botPlayer.ApplyFilterAsync(new TimescaleFilter()
        {
            Speed = 1,
            Rate = value,
            Pitch = 1
        }, guildPlayer.Volume / 100);

        return EmbedHandler.BuildSuccessEmbed($"🕒 Rate set to **{value}**.", user);
    }

    public async Task<Embed> GetLyricsAsync(IGuild guild, IUser user, string artist, string title)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        var lyrics = string.Empty;
        
        // Artist and title are not empty
        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
        {
            lyrics = await LyricsResolver.SearchGeniusAsync(artist, title) ??
                         await LyricsResolver.SearchOvhAsync(artist, title);

            if (string.IsNullOrWhiteSpace(lyrics))
                return EmbedHandler.BuildFailedEmbed($"\\❌ Could not find lyrics for {title}.", user);

            return await EmbedHandler.BuildTrackLyricsEmbedAsync(lyrics, title);
        }

        // Artist and title are empty, take current track
        if (botPlayer.Track == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ No track is playing!", user);

        var match = _videoTitleRegex.Match(botPlayer.Track.Title);

        title = match.Success
            ? match.Groups["title"].Value.Trim()
            : botPlayer.Track.Title;
        artist =
            match.Groups["artist"].Success
                ? match.Groups["artist"].Value.Trim()
                : botPlayer.Track.Author.Replace(" - Topic", "");

        lyrics = await LyricsResolver.SearchGeniusAsync(artist, title) ??
                     await LyricsResolver.SearchOvhAsync(artist, title);

        if (string.IsNullOrWhiteSpace(lyrics))
            return EmbedHandler.BuildFailedEmbed("\\❌ Could not find lyrics for this track.", user);

        return await EmbedHandler.BuildTrackLyricsEmbedAsync(guildPlayer.CurrentTrack, lyrics, title);
    }

    public Embed MoveTrack(IGuild guild, IUser user, int oldIndex, int newIndex)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (!guildPlayer.Tracks.Any())
            return EmbedHandler.BuildFailedEmbed("\\❌ No tracks in queue!", user);

        var track = guildPlayer.Tracks.ElementAtOrDefault(oldIndex - 1);
        var success = guildPlayer.Move(oldIndex, newIndex);
        if (!success)
            return EmbedHandler.BuildFailedEmbed("\\❌ Something went wrong, please confirm the indices.", user);

        return EmbedHandler.BuildSuccessEmbed(
            $"\\✔️**{track?.Track.Title}** has been moved up to position `{newIndex}`.",
            user);
    }

    public Embed ToggleLoop(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        guildPlayer.IsLooping = !guildPlayer.IsLooping;

        return EmbedHandler.BuildSuccessEmbed
            (guildPlayer.IsLooping ? "🔁 Looping enabled \\✔️" : "🔁 Looping disabled \\❌", user);
    }

    public Embed ToggleShuffle(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer)
            || voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel
            || voiceState?.VoiceChannel == null
            || !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        guildPlayer.IsShuffling = !guildPlayer.IsShuffling;

        return EmbedHandler.BuildSuccessEmbed
            (guildPlayer.IsShuffling ? "🔀 Shuffling enabled \\✔️" : "🔀 Shuffling disabled \\❌", user);
    }

    public async Task<Embed> SkipAsync(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != null &&
            voiceState?.VoiceChannel != botPlayer.VoiceChannel ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must join the voice channel to use this command!", user);

        // No track is playing and no tracks in queue
        if (guildPlayer.CurrentTrack == null && !guildPlayer.Tracks.Any())
            return EmbedHandler.BuildFailedEmbed("\\❌ Can't skip nothing!", user);

        // Play next track if any tracks exist in queue
        // Stop current track and let OnTrackEnded handle the shuffling
        // No tracks in queue
        await botPlayer.StopAsync();

        return EmbedHandler.BuildSuccessEmbed("\\✔️Skipping...", user);
    }

    public async Task<Embed> GetNowPlayingAsync(IGuild guild, IUser user)
    {
        // Return if bot is not in voice OR no track is playing
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            botPlayer.Track == null ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not playing a track.",
                user);

        // Send details
        return await EmbedHandler.BuildNowPlayingEmbedAsync(botPlayer.Track, guildPlayer);
    }

    public async Task<Embed> SetVolumeAsync(IGuild guild, IUser user, ushort vol)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != null &&
            voiceState?.VoiceChannel != botPlayer.VoiceChannel ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed
                ("\\❌ You must join the voice channel to use this command!", user);

        await botPlayer.UpdateVolumeAsync(vol);
        guildPlayer.Volume = vol;

        return EmbedHandler.BuildSuccessEmbed($"🔊 Volume set to **{botPlayer.Volume}**.", user);
    }

    public async Task<Embed> PauseAsync(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel)
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must join the voice channel to use this command!", user);

        if (botPlayer.Track == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ No track is playing!", user);

        if (botPlayer.PlayerState == PlayerState.Paused)
            return EmbedHandler.BuildFailedEmbed("\\❌ Track is already paused!", user);

        await botPlayer.PauseAsync();
        return EmbedHandler.BuildSuccessEmbed("\\✔️Track paused.", user);
    }

    public async Task<Embed> ResumeAsync(IGuild guild, IUser user)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != null && voiceState?.VoiceChannel != botPlayer.VoiceChannel)
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must join the voice channel to use this command!", user);

        if (botPlayer.Track == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ No track is playing!", user);

        if (botPlayer.PlayerState == PlayerState.Playing)
            return EmbedHandler.BuildFailedEmbed("\\❌ Track is already playing!", user);

        await botPlayer.ResumeAsync();
        return EmbedHandler.BuildSuccessEmbed("\\✔️Track resumed.", user);
    }

    public async Task<(Embed, LazyPaginator)> GetQueueListAsync(IGuild guild, IUser user)
    {
        // Return if bot is not in voice
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return (EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel.", user), null);

        if (botPlayer.Track == null && !guildPlayer.Tracks.Any())
            return (EmbedHandler.BuildFailedEmbed("\\❌ Nothing in queue.", user), null);

        if (!guildPlayer.Tracks.Any())
            return (await GetNowPlayingAsync(guild, user), null);

        var paginator = EmbedHandler.BuildQueueListPaginator(user, guildPlayer, botPlayer.Track);

        return (null, paginator);
    }

    public async Task<Embed> RemoveQueueAsync(IGuild guild, IUser user, int index)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != null &&
            voiceState?.VoiceChannel != botPlayer.VoiceChannel ||
            !_guildPlayer.TryGetValue(guild.Id, out var guildPlayer))
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must join the voice channel to use this command!", user);

        // Check if queue has any items
        if (!guildPlayer.Tracks.Any())
            return EmbedHandler.BuildFailedEmbed("\\❌ There's nothing to remove!", user);

        else
        {
            // Currently, the index here must not be 0! We'll subtract it after
            if (index > guildPlayer.Tracks.Count || index < 1)
                return EmbedHandler.BuildFailedEmbed("\\❌ Invalid index", user);

            index--;

            // Get details of the track
            var removingTrack = guildPlayer.RemoveAt(index);

            return await EmbedHandler.BuildRemovedQueueEmbedAsync(removingTrack, user);
        }
    }

    public async Task<Embed> SeekAsync(IGuild guild, IUser user, TimeSpan ts)
    {
        // Get user's voice state
        var voiceState = user as IVoiceState;

        // Return if bot is not in voice OR user is not in the same voice channel
        if (!_lavaNode.TryGetPlayer(guild, out var botPlayer) ||
            voiceState?.VoiceChannel != null &&
            voiceState?.VoiceChannel != botPlayer.VoiceChannel)
            return EmbedHandler.BuildFailedEmbed("\\❌ Bot is not in a voice channel or not in the same voice channel.",
                user);

        if (voiceState?.VoiceChannel == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ You must join the voice channel to use this command!", user);

        if (botPlayer.Track == null)
            return EmbedHandler.BuildFailedEmbed("\\❌ No track is playing!", user);

        if (!botPlayer.Track.CanSeek)
            return EmbedHandler.BuildFailedEmbed("\\❌ This track does not support seeking.", user);

        var time = botPlayer.Track.Duration.Hours > 0
            ? $"{ts:hh\\:mm\\:ss}"
            : $"{ts:mm\\:ss}";
        if (botPlayer.Track.Duration < ts)
        {
            var maxTime = botPlayer.Track.Duration.GetTimeFormat();

            return EmbedHandler.BuildFailedEmbed
            ($"\\❌ Can't seek to {time}, max duration is {maxTime}!",
                user);
        }

        // Seek
        await botPlayer.SeekAsync(ts);
        return EmbedHandler.BuildSuccessEmbed($"\\✔️Track playing from {time}.", user);
    }

    #endregion

    #region Private Methods

    private async Task LeaveAsync(LavaPlayer lavaPlayer)
    {
        if (lavaPlayer.Track != null)
            await lavaPlayer.StopAsync();

        // Remove guild player
        _guildPlayer.Remove(lavaPlayer.VoiceChannel.GuildId, out _);

        // Leave voice channel
        try
        {
            await _lavaNode.LeaveAsync(lavaPlayer.VoiceChannel);
        }

        catch
        {
            // ignored
        }
    }

    #endregion

    #region Event Handlers

    public async Task OnTrackEnded(Victoria.EventArgs.TrackEndedEventArgs arg)
    {
        if (arg.Player.VoiceChannel == null ||
            !_guildPlayer.TryGetValue(arg.Player.VoiceChannel.GuildId, out var guildPlayer))
            return;

        // Loop? Loop current track (if track is not skipped)
        if (guildPlayer.IsLooping && arg.Reason == TrackEndReason.Finished)
        {
            guildPlayer.LoopingCurrentTrack = true;
            await arg.Player.PlayAsync(arg.Track);
            return;
        }

        guildPlayer.CurrentTrack = null;

        // Get random queue
        if (guildPlayer.IsShuffling && arg.Reason is TrackEndReason.Finished or TrackEndReason.Stopped)
        {
            // Get random int
            var randomIndex = new Random().Next(0, guildPlayer.Tracks.Count - 1);

            // Get the track then remove it from the queue
            guildPlayer.CurrentTrack = guildPlayer.Dequeue(randomIndex);

            await arg.Player.PlayAsync(guildPlayer.CurrentTrack.Track);
            return;
        }

        // Check if there's a next track
        var track = guildPlayer.Dequeue();
        if (track == null)
        {
            // Start InactivityTimer (Disconnect from voice channel after 10 minutes)
            guildPlayer.StartInactivityTimer();

            return;
        }

        // Play track
        await Task.Delay(200);
        await arg.Player.PlayAsync(track.Track);
    }

    public async Task OnTrackStarted(Victoria.EventArgs.TrackStartEventArgs arg)
    {
        if (!_guildPlayer.TryGetValue(arg.Player.VoiceChannel.GuildId, out var guildPlayer))
            return;

        await arg.Player.UpdateVolumeAsync(guildPlayer.Volume);

        // Stop InactivityTimer
        guildPlayer.StopInactivityTimer();

        // Return if it's looping current track (track not skipped)
        if (guildPlayer.LoopingCurrentTrack)
        {
            guildPlayer.LoopingCurrentTrack = false;
            return;
        }

        // Send details
        await arg.Player.TextChannel.SendMessageAsync("",
            embed: await GetNowPlayingAsync(arg.Player.VoiceChannel.Guild, null));
    }

    public async Task OnTrackException(Victoria.EventArgs.TrackExceptionEventArgs arg)
    {
        var embed = EmbedHandler.BuildTrackExceptionEmbed(arg.Player.VoiceChannel, arg.Exception.Message);
        await arg.Player.TextChannel.SendMessageAsync(null, embed: embed);
    }

    public async Task UserVoiceStateUpdatedAsync(SocketUser arg1, SocketVoiceState arg2, SocketVoiceState arg3)
    {
        // arg2 = old vc (should be null if just joined)
        // arg3 = new vc (or just joined)
        // Get the non-null voice channel (just for if statements)
        var voice = arg2.VoiceChannel ?? arg3.VoiceChannel;

        // Get the guild's LavaPlayer if any
        // Check if the voice channel the bot's currently in is just left with the bot
        if (!_lavaNode.TryGetPlayer(voice.Guild, out var lavaPlayer))
            return;

        // If the user is the bot
        if (arg1 == voice.Guild.CurrentUser)
        {
            // Bot was disconnected from the VC through mod powers
            if (!voice.Guild.CurrentUser.VoiceState.HasValue)
                await LeaveAsync(lavaPlayer);
            
            // New VC
            else if (arg2.VoiceChannel != null)
            {
                await lavaPlayer.TextChannel.SendMessageAsync("Bot was moved, disconnecting.");
                await LeaveAsync(lavaPlayer);
            }
        }

        // VC has only the bot in, try to get the lava player and disconnect from voice
        if (((SocketVoiceChannel)lavaPlayer.VoiceChannel).Users.Count == 1)
        {
            await lavaPlayer.TextChannel.SendMessageAsync("No other users in voice channel, disconnecting.");
            await LeaveAsync(lavaPlayer);
        }
    }

    #endregion
}
