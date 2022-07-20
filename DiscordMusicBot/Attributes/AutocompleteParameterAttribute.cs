using Discord.Interactions;

namespace DiscordMusicBot.Attributes;

public class AutocompleteParameterAttribute : AutocompleteAttribute
{   
    public AutocompleteParameterAttribute() : base(typeof(CommandAutocompletionHandler))
    {
    }
}