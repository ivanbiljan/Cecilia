using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cecilia_NET.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Cecilia_NET.Modules;

public class VoiceCommandsModule : ModuleBase<SocketCommandContext>
{
    private readonly MusicPlayer _musicPlayer;

    public VoiceCommandsModule(MusicPlayer player)
    {
        _musicPlayer = player;
    }

    [Command("Join", RunMode = RunMode.Async)]
    [Summary("Joins the voice channel of the user. Alternatively pass in a channel with a ref")]
    public async Task JoinAsync(IVoiceChannel channel = null)
    {
        // Check for pre-existing connection
        var response = Helpers.CeciliaEmbed(Context);
        if (Context.Guild.AudioClient != null)
        {
            if (Context.Guild.AudioClient.ConnectionState != ConnectionState.Disconnected)
            {
                response.AddField("I'm already connected!", "Drag me to a different room if you want to switch.");
                await Context.Channel.SendMessageAsync("", false, response.Build());

                // Delete the user command
                return;
            }
        }

        // If none then check channel is valid
        // This uses compound assignment:
        // If it is not null, nothing happens
        // If it IS null, it is set to the value of the assignment.
        // In this case that checks if the user is connected into a channel
        channel ??= (Context.User as IGuildUser)?.VoiceChannel;

        // Here null means no channel specified and no user is not in a channel
        if (channel == null)
        {
            response.AddField("Please first join a channel! ", "Alternatively specify a valid channel as an argument.");
            await Context.Channel.SendMessageAsync("", false, response.Build());
            // Delete the user command

            return;
        }

        // Now we have a valid context, channel and guild
        var client = await channel.ConnectAsync(true);

        _musicPlayer.RegisterAudioClient(Context.Guild.Id, client, channel.Id);

        // Display connection message
        response.AddField("I've Connected!", "Thanks for inviting me!", true);
        await Context.Channel.SendMessageAsync("", false, response.Build());

        // Delete the user command
    }

    [Command("Leave", RunMode = RunMode.Async)]
    [Summary(
        "Leaves the voice channel connected to one. Can only be executed by people with the KickMembers permission if the queue is not empty")]
    public async Task LeaveAsync()
    {
        // Delete the user command

        // Check validity of command
        if (!Helpers.IsChannelValid(Context, _musicPlayer))
        {
            return;
        }

        var canExecute = false;
        // It thinks this can fail but it cant
        // Check if they can kick members as a show of admin
        if (Context.User is SocketGuildUser guildUser)
        {
            foreach (var role in guildUser.Roles.ToList())
            {
                // TODO: MAKE THIS A CONFIG PARAMETER
                if (role.Permissions.KickMembers)
                {
                    canExecute = true;

                    break;
                }
            }
        }
        else
        {
            // Just incase something goes oddly wrong. this will basically never hit
            return;
        }

        if (canExecute || _musicPlayer.ActiveAudioClients[Context.Guild.Id]?.Queue.Count == 0)
        {
            // Check for connection
            if (Context.Guild.AudioClient == null ||
                Context.Guild.AudioClient.ConnectionState == ConnectionState.Disconnected)
            {
                return;
            }
            // There is a connect in this guild

            // Delete now-playing as it is now out of date
            await _musicPlayer.DeleteNowPlayingMessage(Context);

            // Disconnect
            Console.WriteLine("Leaving!");
            await _musicPlayer.ActiveAudioClients[Context.Guild.Id].Client.StopAsync();
            Console.WriteLine("Ive left");
            _musicPlayer.RemoveAudioClient(Context.Guild.Id);

            // Cleanup audio cache
            // If there are no active clients
            if (_musicPlayer.ActiveAudioClients.Count == 0)
            {
                // Get correct directory
                var directoryPrefix = Bot.OsPlatform == OSPlatform.Windows ? @"AudioCache\" : "AudioCache/";

                // Close file streams so files can be deleted
                _musicPlayer.CloseFileStreams();

                // Clear it
                foreach (var file in Directory.GetFiles(directoryPrefix))
                {
                    File.Delete(file);
                }
            }

            // Now we have disconnected
            var response = Helpers.CeciliaEmbed(Context);
            response.AddField("I'm off!", "See you next time!");
            await Context.Channel.SendMessageAsync("", false, response.Build());
        }
        else
        {
            var response = Helpers.CeciliaEmbed(Context);
            response.AddField("Invalid permissions!", "You can't do that. Do --help to find out why.");
            await Context.Channel.SendMessageAsync("", false, response.Build());
        }
    }

    [Command("Pause", RunMode = RunMode.Async)]
    [Summary("Pauses playback")]
    public async Task PauseAsync()
    {
        // Delete the user command

        // Check validity of command
        if (!Helpers.IsChannelValid(Context, _musicPlayer))
        {
            return;
        }

        // Check we aren't already paused
        if (!_musicPlayer.ActiveAudioClients[Context.Guild.Id].Paused)
        {
            // Log
            await Bot.CreateLogEntry(LogSeverity.Info, "Commands", "Pause Requested!");
            // Set paused boolean
            _musicPlayer.ActiveAudioClients[Context.Guild.Id].Paused = true;
            // output paused message
            var response = Helpers.CeciliaEmbed(Context);
            response.AddField("Pausing playback!", "Take a break and recharge!");
            await Context.Channel.SendMessageAsync("", false, response.Build());
        }
    }

    [Command("Play", RunMode = RunMode.Async)]
    [Summary("Adds a song to the play queue")]
    public async Task PlayAsync([Remainder] [Summary("The URL to play.")] string uri)
    {
        // Delete user command if not deleted by join

        var response = Helpers.CeciliaEmbed(Context);
        // No playlist links yet
        if (uri.Contains("playlist?list=", StringComparison.Ordinal))
        {
            response.AddField("Not yet supported! :-(", "I can't process playlist links yet, but I will do soon!");
            await Context.Channel.SendMessageAsync("", false, response.Build());

            return;
        }

        // Check if connected to voice
        if (Context.Guild.AudioClient == null || Context.Guild.AudioClient.ConnectionState != ConnectionState.Connected)
        {
            // We aren't connected. But if the user is in a channel auto connect
            var user = Context.User as SocketGuildUser;
            if (user?.VoiceChannel != null)
            {
                await JoinAsync(user.VoiceChannel);
            }
            // Else we fail out
            else
            {
                response.AddField("I'm not connected!", "Join a voice channel and re-run the command.");
                await Context.Channel.SendMessageAsync("", false, response.Build());

                return;
            }
            // Then continue if connected
        }

        // Check validity of command
        if (!Helpers.IsChannelValid(Context, _musicPlayer))
        {
            return;
        }

        // Check youtube link valid
        if ((uri.Contains("http:") || uri.Contains("https:")) && !uri.Contains("youtube.com"))
        {
            response.AddField(
                "Not yet supported! :-(",
                "I only support Youtube currently! More platforms coming soon!");
            await Context.Channel.SendMessageAsync("", false, response.Build());

            return;
        }

        // Send a searching embed
        response.AddField("Searching...", "Give me a minute to look that up!");
        var searchEmbed = await Context.Channel.SendMessageAsync("", false, response.Build());

        // Start youtube explode
        var youtube = new YoutubeClient();
        // Get video metadata & download thumbnail
        var video = await youtube.Videos.GetAsync(VideoId.Parse(uri));

        // get streams
        var streams = await youtube.Videos.Streams.GetManifestAsync(video.Id);

        // 3. Add to queue and send stream info so download can be processed
        await Context.Channel.DeleteMessageAsync(searchEmbed.Id);
        var builder = await _musicPlayer.AddSongToQueue(
            Context,
            video,
            streams.GetAudioOnlyStreams().GetWithHighestBitrate(),
            uri);

        // 4. Notify added
        await Context.Channel.SendMessageAsync("", false, builder.Build());

        // Now play            
        await _musicPlayer.PlayAudio(Context);
    }

    [Command("Queue", RunMode = RunMode.Async)]
    [Summary("List the queued songs")]
    public async Task QueueAsync()
    {
        // Get the client for this server
        MusicPlayer.WrappedAudioClient? audioClient;
        try
        {
            audioClient = _musicPlayer.ActiveAudioClients[Context.Guild.Id];
        }
        catch (KeyNotFoundException)
        {
            audioClient = null;
        }

        // If there is no connection, or we are disconnected, or there is no queue
        var embedBuilder = Helpers.CeciliaEmbed(Context);
        if (audioClient == null ||
            audioClient.Client.ConnectionState == ConnectionState.Disconnected ||
            audioClient.Queue.Count == 0)
        {
            // Say no queue and leave
            embedBuilder.AddField("The queue is empty!", "Add some songs with the play command!");

            await Context.Channel.SendMessageAsync("", false, embedBuilder.Build());

            // Delete message

            return;
        }

        // There is a connection & queue
        var queuePosition = 1;
        embedBuilder.WithTitle($"Current Queue (Up to {Helpers.MAX_FIELD_IN_EMBED})");
        foreach (var item in audioClient.Queue)
        {
            // building explicitly due to size for readability
            var video = item.MetaData;

            var fieldValue = "";
            var correctedSeconds = video.Duration?.Seconds <= 9
                ? $"0{video.Duration?.Seconds}"
                : video.Duration?.Seconds.ToString();
            fieldValue += " Length: " + video.Duration?.Minutes + ":" + correctedSeconds;
            fieldValue += $" [View on YT]({video.Url})";

            embedBuilder.AddField($"{queuePosition}: {video.Title}", fieldValue);
            ++queuePosition;
            // Max queue embed size
            if (queuePosition == Helpers.MAX_FIELD_IN_EMBED)
            {
                break;
            }
        }

        await Context.Channel.SendMessageAsync("", false, embedBuilder.Build());
        // Delete message
    }

    [Command("Play", RunMode = RunMode.Async)]
    [Summary("Resumes playback")]
    public async Task ResumeAsync()
    {
        // Delete the user command

        // Check validity of command
        if (!Helpers.IsChannelValid(Context, _musicPlayer))
        {
            return;
        }

        // Check we are paused
        if (_musicPlayer.ActiveAudioClients[Context.Guild.Id].Paused)
        {
            // Log
            await Bot.CreateLogEntry(LogSeverity.Info, "Commands", "Resume Requested!");
            // Unset pause boolean
            _musicPlayer.ActiveAudioClients[Context.Guild.Id].Paused = false;
            // Send message
            var response = Helpers.CeciliaEmbed(Context);
            response.AddField("Resuming playback!", "Here come the bangers!");
            await Context.Channel.SendMessageAsync("", false, response.Build());
        }
    }

    [Command("Skip", RunMode = RunMode.Async)]
    [Summary("Skips current song")]
    public async Task SkipAsync()
    {
        // Delete the user command

        // Check the validity of the user executing this command
        if (!Helpers.IsChannelValid(Context, _musicPlayer))
        {
            return;
        }

        if (_musicPlayer.ActiveAudioClients[Context.Guild.Id].Queue.Count == 0)
        {
            return;
        }

        _musicPlayer.ActiveAudioClients[Context.Guild.Id].Skip = true;

        var response = Helpers.CeciliaEmbed(Context);
        response.AddField(
            "Skipping Song!",
            _musicPlayer.ActiveAudioClients[Context.Guild.Id]?.Queue.Count > 1
                ? "Onto the next one..."
                : "Spin up some more songs with the play command!");

        await Context.Channel.SendMessageAsync("", false, response.Build());
    }
}