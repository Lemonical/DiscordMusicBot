using Discord;

namespace DiscordMusicBot.Extensions;

public static class UserExtensions
{
    public static string GetDisplayName(this IUser user, bool withDiscrim = false)
    {
        var name = user is IGuildUser guildUser ? guildUser.DisplayName : user.Username;

        return withDiscrim ? name + $" (#{user.DiscriminatorValue})" : name;
    }
    
    public static string GetDisplayAvatar(this IUser user)
        => user is IGuildUser guildUser 
            ? guildUser.GetDisplayAvatarUrl(size: 512)
            : user.GetAvatarUrl(size: 512) ?? user.GetDefaultAvatarUrl();
}