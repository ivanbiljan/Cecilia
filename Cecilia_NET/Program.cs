﻿// Cecilia.NET
// Yet another music bot written in Discord.NET
// By Michael Grime 12/08/2020

// Console access
// TAP access
// Discord API
// Discord web connection
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cecilia_NET.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SpotifyAPI.Web;
using YouTubeSearch;
using static System.String;    // Speeds up string comparison calls

namespace Cecilia_NET
{
    public class Bot
    {
        // Transfer to an async main method after acquiring token
        public static void Main(string[] args)
        {
            // Find out platform
            OsPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows : OSPlatform.Linux;

            BotConfig = new Config();
            // Extract config from .json in root directory
            while (Compare(BotConfig.Token, "", StringComparison.Ordinal) == 0)
            {
                try
                {
                    var rawConfig = System.IO.File.ReadAllText(@"config.json").Replace(Environment.NewLine,"");
                    BotConfig = JsonConvert.DeserializeObject<Config>(rawConfig);
                }
                catch (System.IO.FileNotFoundException)
                {
                    Console.WriteLine("ERROR: Fill in the file (config.json) in the directory of the DLL. Press escape to quit! Press any other key to hot reload!");
                    if (Console.ReadKey().Key == ConsoleKey.Escape)
                    {
                        return;
                    }
                }
            }

            if (!Directory.Exists("AudioCache"))
            {
                System.IO.Directory.CreateDirectory("AudioCache");
            }
            
            // Transfer to async
            // Catching all exceptions that reach here just to clean up then close
            new Bot().MainASync(BotConfig).GetAwaiter().GetResult();
        }

        // Allows for async calls to Discord.NET
        private async Task MainASync(Config config)
        {
            // Create base for client
            _client = new DiscordSocketClient();
            
            // Link to logging method
            _client.Log += LogAsync;
            
            // Request login!
            await _client.LoginAsync(TokenType.Bot, config.Token);
            // UNTIL THIS LINE ^ COMPLETES CLIENT IS NOT IN A USABLE STATE
      
            // Create command service and handler                                
            _commandService = new CommandService(new CommandServiceConfig        
            {                                                                    
                CaseSensitiveCommands = false,                                   
                DefaultRunMode = RunMode.Async,                                  
                IgnoreExtraArgs = false,                                         
                LogLevel = LogSeverity.Info,    // TODO monitor this,            
                SeparatorChar = ' ',                                             
                ThrowOnError = false                                             
            });                                                   
            // Create service injector for Dependencies
            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commandService)
                .AddSingleton<MusicPlayer>()    // For music audio playback
                .BuildServiceProvider();

            _commandHandler = new CommandHandler(_client,_commandService,_services,config.Prefix);  
            // Register commands
            await _commandHandler.InstallCommandsAsync();

            // Now login is okay and commands are registered we can start               
            await _client.StartAsync();

            await _client.SetGameAsync("some tunes!", null, ActivityType.Listening);
            
            
            // Block this main task until program is closed
            await Task.Delay(-1);
        }
        
        // Log to console for now
        // TODO: Link to a proper logging system. Perhaps even something GUI based for bot management.
        public static Task CreateLogEntry(LogSeverity severity,string source,string msg)
        {
            var logMsg = new LogMessage(severity,source,msg,null);
            LogAsync(logMsg);
            return Task.CompletedTask;
        }
        public static Task LogAsync(LogMessage msg)
        {
            // Log to console
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
        // Data members

        // The main connection to Discord
        private DiscordSocketClient _client;
        // Collection of commands from different modules loaded by handler
        private CommandService _commandService;
        // Manages the loading of modules and connection to _client
        private CommandHandler _commandHandler;
        // Manages dependency injection
        private IServiceProvider _services;
        // Config
        public static Config BotConfig { get; set; }
        public static OSPlatform OsPlatform { get; set; }


    }
}