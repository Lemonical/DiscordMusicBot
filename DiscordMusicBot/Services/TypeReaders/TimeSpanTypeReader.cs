using Discord.Commands;

namespace DiscordMusicBot.Services.TypeReaders;

public class TimeSpanTypeReader : TypeReader
{
    public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
    {
        // 369 but in seconds
        if (!input.Contains(':') && input.Length > 2 && double.TryParse(input, out var seconds))
            return Task.FromResult(TypeReaderResult.FromSuccess(TimeSpan.FromSeconds(seconds)));

        if (input.Contains(':'))
        {
            var split = input.Split(':');

            if (split.Length > 3)
                return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Can't seek to days"));

            int? sec = null;
            int? min = null;
            int? hr = null;

            switch (split.Length)
            {
                case 2:
                    foreach (var str in split)
                    {
                        if (!int.TryParse(str, out var time))
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed,
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
                            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed,
                                "Invalid input"));

                        if (hr.HasValue)
                            min = time;
                        else
                            hr = time;
                    }

                    break;
            }

            return Task.FromResult(TypeReaderResult.FromSuccess(new TimeSpan(hr.GetValueOrDefault(),
                min.GetValueOrDefault(), sec.GetValueOrDefault())));
        }

        else if (input.Length < 3 && int.TryParse(input, out var sec))
            return Task.FromResult(TypeReaderResult.FromSuccess(new TimeSpan(0, 0, sec)));

        return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Invalid input"));
    }
}