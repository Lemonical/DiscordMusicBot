using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Reflection;
using Discord.Interactions;
using DiscordMusicBot.Services.TypeReaders;
using DiscordMusicBot.Services.TypeConverters;

namespace DiscordMusicBot.Handlers;

public class CommandHandler
{
    private readonly DiscordSocketClient _client;
    private readonly CommandService _commandService;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _services;

    public CommandHandler(DiscordSocketClient client, CommandService commandService,
        InteractionService interactionService, IServiceProvider services)
    {
        _client = client;
        _commandService = commandService;
        _interactionService = interactionService;
        _services = services;
    }

    public async Task InstallAsync()
    {
        _commandService.AddTypeReader<TimeSpan>(new TimeSpanTypeReader());
        _interactionService.AddTypeConverter<TimeSpan>(new TimeSpanTypeConverter());

        await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        _client.MessageReceived += HandleCommandAsync;
        _client.InteractionCreated += HandleInteractionAsync;
        _commandService.Log += LogAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction arg)
    {
        try
        {
            // Create an execution context that matches the generic type parameter of your InteractionModuleBase<T> modules
            var context = new SocketInteractionContext(_client, arg);
            
            if (arg.Type is InteractionType.MessageComponent or InteractionType.ModalSubmit &&
                await RespondToComponentsAsync(arg).ConfigureAwait(false))
                return;

            var preconditionResult = await CheckPreconditionsAsync(arg, context);
            if (preconditionResult != null)
            {
                await arg.RespondAsync(embed: preconditionResult);
                return;
            }
            
            var result = await _interactionService.ExecuteCommandAsync(context, _services);

            if (!result.IsSuccess)
                await arg.RespondAsync(embed: EmbedHandler.BuildFailedEmbed(result.ErrorReason, arg.User));
        }

        catch (Exception ex)
        {
            Console.WriteLine(ex);

            // If a Slash Command execution fails it is most likely that the original interaction acknowledgement will persist. It is a good idea to delete the original
            // response, or at least let the user know that something went wrong during the command execution.
            if (arg.Type == InteractionType.ApplicationCommand)
                await arg.GetOriginalResponseAsync()
                    .ContinueWith(async msg =>
                        await msg.Result.DeleteAsync());
        }
    }

    private async Task HandleCommandAsync(SocketMessage parameterMessage)
    {
        // Don't handle the command if it is a system message
        if (parameterMessage is not SocketUserMessage message || parameterMessage.Author.IsBot) return;

        CommandContext context = new(_client, message);

        // Command handling
        var argPos = 0;

        if (message.HasStringPrefix(BotConfig.Load().Prefix, ref argPos))
        {
            var result = await _commandService.ExecuteAsync(context, argPos, _services);

            if (!result.IsSuccess && result.Error != CommandError.UnknownCommand && result.ErrorReason != "")
                await message.Channel
                    .SendMessageAsync("", embed: EmbedHandler.BuildFailedEmbed(result.ErrorReason, context.User))
                    .ConfigureAwait(false);
        }
    }
    
    private Task<bool> RespondToComponentsAsync(SocketInteraction arg)
    {
        switch (arg)
        {
            // Buttons
            case SocketMessageComponent button:
                switch (button.Data.CustomId)
                {
                    case "deleteMessages":
                    case "◀":
                    case "▶":
                    case "🔢":
                    case "🛑":
                        return Task.FromResult(true);
                    default:
                        return Task.FromResult(false);
                }
        }

        return Task.FromResult(false);
    }

    private async Task<Embed> CheckPreconditionsAsync(IDiscordInteraction arg, SocketInteractionContext ctx)
    {
        Discord.Interactions.PreconditionResult result = null;
        
        switch (arg.Type)
        {
            case InteractionType.ApplicationCommand:
                if (arg.GetType() == typeof(SocketSlashCommand))
                {
                    var data = arg.Data as SocketSlashCommandData;
                    var commandName = $"{data?.Name} {data?.Options.FirstOrDefault()?.Name}";
                    var slashCommandInfo = _interactionService.SlashCommands
                        .FirstOrDefault(x => x.ToString() == commandName);
                    
                    if (slashCommandInfo != null)
                        result = await slashCommandInfo.CheckPreconditionsAsync(ctx, _services);
                }
                break;
            case InteractionType.Ping:
            case InteractionType.MessageComponent:
            case InteractionType.ApplicationCommandAutocomplete:
            case InteractionType.ModalSubmit:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return (result is { IsSuccess: false } 
            ? EmbedHandler.BuildFailedEmbed(result.ErrorReason, ctx.User) 
            : null)!;
    }

    private Task LogAsync(LogMessage message)
    {
        Console.ForegroundColor = message.Severity switch
        {
            LogSeverity.Critical or LogSeverity.Error => ConsoleColor.Red,
            LogSeverity.Warning => ConsoleColor.Blue,
            LogSeverity.Info => ConsoleColor.Green,
            _ => ConsoleColor.White,
        };
        Console.WriteLine($"[{message.Severity}] {message.Source}: {message.Exception?.ToString() ?? message.Message}");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}