using Victoria;

namespace DiscordMusicBot.GuildPlayer
{
    public sealed class GuildAudio : IDisposable
    {
        public delegate Task LeaveMethod(LavaPlayer lavaPlayer);
        public GuildTrack CurrentTrack { get; set; }
        public List<GuildTrack> Tracks { get; } = new();
        public bool IsLooping { get; set; }
        public bool LoopingCurrentTrack { get; set; }
        public bool IsShuffling { get; set; }
        public ushort Volume { get; set; } = 100;

        private readonly Timer _inactivityTimer;
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(15);
        private bool _disposed;

        public GuildAudio(LavaPlayer player, LeaveMethod leave)
        {
            _inactivityTimer = new(x => Disconnect(x, leave), player, _inactivityTimeout, TimeSpan.FromSeconds(0));
        }

        public void Enqueue(GuildTrack track)
            => Tracks.Add(track);

        public void EnqueueRange(IEnumerable<GuildTrack> tracks)
            => Tracks.AddRange(tracks);
        
        public bool Move(int oldIndex, int newIndex)
        {
            oldIndex--;
            newIndex--;
            if (!Tracks.Any() || oldIndex >= Tracks.Count || oldIndex < 0 || newIndex >= Tracks.Count || newIndex < 0)
                return false;
            
            var trackToMove = RemoveAt(oldIndex);
            Tracks.Insert(newIndex, trackToMove);
            return true;
        }

        public GuildTrack Dequeue(int index = 0)
        {
            if (!Tracks.Any() || index >= Tracks.Count || index < 0)
                return null;

            var track = Tracks.ElementAtOrDefault(index);
            Tracks.RemoveAt(index);
            CurrentTrack = track;
            return track;
        }

        public GuildTrack RemoveAt(int index)
        {
            if (!Tracks.Any() || index >= Tracks.Count || index < 0)
                return null;

            var track = Tracks.ElementAtOrDefault(index);
            Tracks.RemoveAt(index);
            return track;
        }

        private async void Disconnect(object state, LeaveMethod leave)
        {
            if (state is not LavaPlayer lavaPlayer || lavaPlayer.VoiceChannel == null)
                return;

            await leave(lavaPlayer);
            Dispose();
        }

        public void StopInactivityTimer()
        {
            _inactivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void StartInactivityTimer()
        {
            _inactivityTimer.Change(_inactivityTimeout, TimeSpan.FromSeconds(0));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                CurrentTrack = null;
                Tracks.Clear();
                StopInactivityTimer();
                _inactivityTimer.Dispose();
            }

            _disposed = true;
        }

        ~GuildAudio()
        {
            Dispose(false);
        }
    }
}
