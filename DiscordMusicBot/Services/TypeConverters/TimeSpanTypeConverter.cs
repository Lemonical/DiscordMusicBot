using Discord;
using Discord.Interactions;

namespace DiscordMusicBot.Services.TypeConverters;

public class TimeSpanTypeConverter : TypeConverter
{
    public override bool CanConvertTo(Type type) => type == typeof(TimeSpan);

    public override ApplicationCommandOptionType GetDiscordType() => ApplicationCommandOptionType.String;
    
    public override Task<TypeConverterResult> ReadAsync(IInteractionContext context, IApplicationCommandInteractionDataOption option, IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(option.Value.ToString()))
            return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed,
                "Input empty!"));
        
        var input = option.Value.ToString();
        
        // 369 but in seconds
        if (!input.Contains(':') && input.Length > 2 && double.TryParse(input, out var seconds))
            return Task.FromResult(TypeConverterResult.FromSuccess(TimeSpan.FromSeconds(seconds)));

        if (input.Contains(':'))
        {
            var split = input.Split(':');

            if (split.Length > 3)
                return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Can't seek to days"));

            int? sec = null;
            int? min = null;
            int? hr = null;

            switch (split.Length)
            {
                case 2:
                    foreach (var str in split)
                    {
                        if (!int.TryParse(str, out var time))
                            return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed,
                                "Invalid input"));

                        if (min.HasValue)
                            sec = time;
                        else
                            min = time;
                    }

                    break;
                case 3:
                    foreach (var str in split)
                    {
                        if (!int.TryParse(str, out var time))
                            return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed,
                                "Invalid input"));

                        if (hr.HasValue)
                            min = time;
                        else
                            hr = time;
                    }

                    break;
            }

            return Task.FromResult(TypeConverterResult.FromSuccess(new TimeSpan(hr.GetValueOrDefault(),
                min.GetValueOrDefault(), sec.GetValueOrDefault())));
        }

        else if (input.Length < 3 && int.TryParse(input, out var sec))
            return Task.FromResult(TypeConverterResult.FromSuccess(new TimeSpan(0, 0, sec)));

        return Task.FromResult(TypeConverterResult.FromError(InteractionCommandError.ParseFailed, "Invalid input"));
    }
}