using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using PacManBot.Games;
using PacManBot.Constants;
using PacManBot.Extensions;

namespace PacManBot.Services
{
    /// <summary>
    /// Routinely executes specific actions such as connection checks.
    /// </summary>
    public class SchedulingService
    {
        private readonly DiscordShardedClient client;
        private readonly StorageService storage;
        private readonly LoggingService logger;
        private readonly GameService games;

        public List<Timer> timers;
        private CancellationTokenSource cancelShutdown = new CancellationTokenSource();


        public SchedulingService(DiscordShardedClient client, StorageService storage, LoggingService logger, GameService games)
        {
            this.client = client;
            this.storage = storage;
            this.logger = logger;
            this.games = games;

            timers = new List<Timer>
            {
                new Timer(CheckConnection, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)),
                new Timer(DeleteOldGames, null, TimeSpan.Zero, TimeSpan.FromSeconds(5))
            };

            // Events
            client.ShardConnected += OnShardConnected;
        }



        private Task OnShardConnected(DiscordSocketClient shard)
        {
            if (client.AllShardsConnected())
            {
                cancelShutdown.Cancel();
                cancelShutdown = new CancellationTokenSource();
            }
            return Task.CompletedTask;
        }



        public async void CheckConnection(object state)
        {
            if (client.AllShardsConnected()) return;

            await logger.Log(LogSeverity.Info, LogSource.Scheduling, "A shard is disconnected. Waiting for reconnection...");

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), cancelShutdown.Token);
                await logger.Log(LogSeverity.Critical, LogSource.Scheduling, "Reconnection timed out. Shutting down...");
                Environment.Exit(ExitCodes.ReconnectionTimeout);
            }
            catch (OperationCanceledException)
            {
                await logger.Log(LogSeverity.Info, LogSource.Scheduling, "All shards reconnected. Shutdown aborted");
            }
        }


        public async void DeleteOldGames(object state)
        {
            var now = DateTime.Now;
            int count = 0;

            var removedChannelGames = new List<IChannelGame>();

            var expiredGames = games.AllGames.Where(g => now - g.LastPlayed > g.Expiry).ToArray();
            foreach (var game in expiredGames)
            {
                count++;
                game.State = State.Cancelled;
                games.Remove(game, false);

                if (game is IChannelGame cGame) removedChannelGames.Add(cGame);
            }


            if (count > 0)
            {
                await logger.Log(LogSeverity.Info, LogSource.Scheduling, $"Removed {count} expired game{"s".If(count > 1)}");
            }


            if (client?.LoginState == LoginState.LoggedIn)
            {
                foreach (var game in removedChannelGames)
                {
                    try
                    {
                        var gameMessage = await game.GetMessage();
                        if (gameMessage != null) await gameMessage.ModifyAsync(game.GetMessageUpdate(), Bot.DefaultOptions);
                    }
                    catch (HttpException) { } // Something happened to the message, we can ignore it
                }
            }
        }
    }
}
