using Newtonsoft.Json;

namespace DiscordMusicBot;

public class BotConfig
{
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "botdata", "config.json");
    
    public string Token { get; set; }
    public string Prefix { get; set; }
    public string Game { get; set; }
    public string LavalinkIp { get; set; }
    public int LavalinkPort { get; set; }
    public string LavalinkPassword { get; set; }
    
    public BotConfig()
    {
        Prefix = "!";
        LavalinkIp = "127.0.0.1";
        LavalinkPort = 2333;
        LavalinkPassword = "youshallnotpass";
    }
    
    public void Save()
    {
        File.WriteAllText(Path, ToJson());
    }

    public static BotConfig Load()
    {
        CreateFolder();
        if (!File.Exists(Path))
            new BotConfig().Save();
        
        return JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(Path));
    }

    private static void CreateFolder()
    {
        Directory.CreateDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "botdata"));
    }

    private string ToJson()
        => JsonConvert.SerializeObject(this, Formatting.Indented);
}
