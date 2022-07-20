using Discord;
using Discord.Interactions;
using DiscordMusicBot.Extensions;
using Victoria.Responses.Search;

namespace DiscordMusicBot.Handlers;

public class CommandAutocompletionHandler : AutocompleteHandler
{
    public AudioService Audio { get; set; }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        return parameter.Name switch
        {
            "query" => await GenerateYoutubeSearchResultsAsync(autocompleteInteraction),
            _ => AutocompletionResult.FromSuccess()
        };
    }

    private async Task<AutocompletionResult> GenerateYoutubeSearchResultsAsync(
        IAutocompleteInteraction autocompleteInteraction)
    {
        var enumValue = autocompleteInteraction.Data.Options.FirstOrDefault(x =>
            x.Name.Equals("service", StringComparison.OrdinalIgnoreCase))?.Value.ToString();
        var query = autocompleteInteraction.Data.Current.Value.ToString();

        if (string.IsNullOrWhiteSpace(query))
            return AutocompletionResult.FromSuccess();
        
        var searchType = ResolveSearchType(enumValue!, query);

        var searchResults = (await Audio.SearchAsync(query, searchType)).Tracks;
        if (searchResults == null || !searchResults.Any())
            return AutocompletionResult.FromSuccess();

        try
        {
            var results = searchResults.Take(25)
                .Select(result =>
                    new AutocompleteResult(
                        $@"[{result.Duration.GetTimeFormat()}] {(result.Title.Length > 70 
                            ? result.Title[..67] + "..."
                            : result.Title)}", result.Url))
                .ToList()
                .Distinct();

            return AutocompletionResult.FromSuccess(results);
        }

        catch (Exception)
        {
            return AutocompletionResult.FromSuccess();
        }
    }

    private SearchType ResolveSearchType(string enumValue, string query)
    {
        var service = Uri.IsWellFormedUriString(query, UriKind.Absolute) 
            ? SearchType.Direct
            : SearchType.YouTube;
        
        if (!string.IsNullOrWhiteSpace(enumValue))
            service = Enum.Parse<SearchType>(enumValue, true);

        return service;
    }
}