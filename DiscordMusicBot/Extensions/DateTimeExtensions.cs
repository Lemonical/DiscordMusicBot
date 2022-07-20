namespace DiscordMusicBot.Extensions;

public static class DateTimeExtensions
{
    public static string GetTimeFormat(this TimeSpan duration)
    {
        return duration.Hours > 0
            ? $"{duration:hh\\:mm\\:ss}"
            : $"{duration:mm\\:ss}";
    }
}