using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PacManBot.Services;
using PacManBot.Constants;


//Made by Samrux for fun
//GitHub repo: https://github.com/Samrux/Pac-Man-Bot

namespace PacManBot
{
    public static class Program
    {
        static async Task Main()
        {
            // Check files
            if (!File.Exists(BotFile.Config)) throw new Exception($"Missing required file {BotFile.Config}: Bot can't run");
            if (!File.Exists(BotFile.Contents)) throw new Exception($"Missing required file {BotFile.Contents}: Bot can't run.");

            string[] secondaryFile = new string[] { BotFile.Prefixes, BotFile.Scoreboard, BotFile.WakaExclude };
            for (int i = 0; i < secondaryFile.Length; i++)
            {
                if (!File.Exists(secondaryFile[i]))
                {
                    File.Create(secondaryFile[i]).Dispose();
                    Console.WriteLine($"Created missing file \"{secondaryFile[i]}\"");
                }
            }

            // Set up configurations
            var botConfig = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText(BotFile.Config));
            if (string.IsNullOrWhiteSpace(botConfig.discordToken)) throw new Exception($"Missing bot token in {BotFile.Config}");


            var clientConfig = new DiscordSocketConfig
            {
                TotalShards = botConfig.shardCount,
                LogLevel = botConfig.clientLogLevel,
                MessageCacheSize = botConfig.messageCacheSize
            };

            var client = new DiscordShardedClient(clientConfig);


            var commandConfig = new CommandServiceConfig
            {
                DefaultRunMode = RunMode.Async,
                LogLevel = botConfig.commandLogLevel
            };

            var commands = new CommandService(commandConfig);
            await commands.AddModulesAsync(Assembly.GetEntryAssembly());


            // Set up services
            var services = new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton(commands)
                .AddSingleton(botConfig)
                .AddSingleton<LoggingService>()
                .AddSingleton<StorageService>()
                .AddSingleton<CommandHandler>()
                .AddSingleton<ReactionHandler>()
                .AddSingleton<ScriptingService>();

            var provider = services.BuildServiceProvider();
            foreach (var service in services) provider.GetRequiredService(service.ServiceType);


            // Let's go
            await new Bot(botConfig, provider).StartAsync();

            await Task.Delay(-1);
        }
    }
}
