using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using Microsoft.Extensions.DependencyInjection;
using Victoria;

namespace DiscordMusicBot.Services;

public class DiscordService
{
    private readonly DiscordSocketClient _client;
    private readonly CommandService _commandService;
    private readonly InteractionService _interactionService;

    private readonly LavaNode _lavaNode;
    private readonly IServiceProvider _services;
    private readonly AudioService _audioService;

    public DiscordService()
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig()
        {
            AlwaysDownloadUsers = true,
            LogLevel = LogSeverity.Debug,
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
            LogGatewayIntentWarnings = true
        });

        _commandService = new CommandService(new CommandServiceConfig()
        {
            LogLevel = LogSeverity.Verbose,
            CaseSensitiveCommands = false
        });

        _interactionService = new InteractionService(_client, new InteractionServiceConfig()
        {
            LogLevel = LogSeverity.Verbose
        });

        _services = BuildServices();
        _lavaNode = _services.GetRequiredService<LavaNode>();
        _audioService = _services.GetRequiredService<AudioService>();

        SubscribeOrLogEvents();
    }

    private void SubscribeOrLogEvents()
    {
        _client.Log += LogAsync;
        _lavaNode.OnLog += LogAsync;
        _lavaNode.OnTrackEnded += _audioService.OnTrackEnded;
        _lavaNode.OnTrackStarted += _audioService.OnTrackStarted;
        _lavaNode.OnTrackException += _audioService.OnTrackException;
        
        _client.Ready += ReadyAsync;
        _client.UserVoiceStateUpdated += _audioService.UserVoiceStateUpdatedAsync;
    }

    public async Task InitializeAsync()
    {
        await _client.LoginAsync(TokenType.Bot, BotConfig.Load().Token);
        await _client.StartAsync();
        await new CommandHandler(_client, _commandService, _interactionService, _services).InstallAsync();
        await Task.Delay(-1);
    }

    private IServiceProvider BuildServices()
    {
        var botData = BotConfig.Load();

        return new ServiceCollection()
            .AddSingleton(_client)
            .AddSingleton(_commandService)
            .AddSingleton<InteractiveService>()
            .AddSingleton<AudioService>()
            .AddLavaNode(x =>
            {
                x.SelfDeaf = true;
                x.Authorization = botData.LavalinkPassword;
                x.Hostname = botData.LavalinkIp;
                x.Port = Convert.ToUInt16(botData.LavalinkPort);
            })
            .BuildServiceProvider();
    }

    private static Task LogAsync(LogMessage message)
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

    private async Task ReadyAsync()
    {
        var game = BotConfig.Load().Game;
        
        if (!string.IsNullOrWhiteSpace(game))
            await _client.SetGameAsync(game);
        await _interactionService.RegisterCommandsGloballyAsync();

        if (!_lavaNode.IsConnected)
        {
            try
            {
                await _lavaNode.ConnectAsync();
            }

            catch (InvalidOperationException ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
