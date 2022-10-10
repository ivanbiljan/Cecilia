using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using Discord;
using Discord.Audio;
using Discord.Commands;
using Discord.Rest;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Cecilia_NET.Services;

public class MusicPlayer
{
    private MemoryStream _output = new(); // The FFMPEG library ouput stream

    public Dictionary<ulong, WrappedAudioClient?> ActiveAudioClients { get; } = new(); 

    public RestUserMessage? NowPlayingMessage { get; set; } // A reference to the "Now Playing" message

    public async Task<EmbedBuilder> AddSongToQueue(
        SocketCommandContext context,
        Video videoData,
        IStreamInfo streamData,
        string searchTerm)
    {
        var activeClient = ActiveAudioClients[context.Guild.Id]!;

        // THIS METHOD REQUIRES A MUTEX INCASE MULTIPLE SONGS ARE QUEUED UP IN QUICK SUCCESSION
        // Find the mutex for this queue
        var mutex = activeClient.QueueMutex;
        // Wait for it to be free
        mutex.WaitOne(-1);
        await Bot.CreateLogEntry(LogSeverity.Info, "Music Player", "Adding to queue for guild: " + context.Guild.Id);
        // Add song to queue
        activeClient.Queue.AddLast(
            new QueueEntry(searchTerm, videoData, streamData, Helpers.CeciliaEmbed(context)));
        // Release mutex
        await Bot.CreateLogEntry(LogSeverity.Info, "Music Player", "Added to queue for guild: " + context.Guild.Id);
        mutex.ReleaseMutex();

        // create embed
        // Caching so it can be modified for playing message
        // TODO: this might throw
        var activeEmbed = activeClient.Queue.Last.Value.EmbedBuilder;

        activeEmbed.WithImageUrl(videoData.Thumbnails[0].Url);
        activeEmbed.WithTitle("Added song!"); // This can be switched later
        activeEmbed.AddField("Title", $"[{videoData.Title}]({videoData.Url})");
        activeEmbed.AddField("Length", videoData.Duration?.Minutes + " min " + videoData.Duration?.Seconds + " secs");
        activeEmbed.AddField("Uploader", videoData.Author);
        activeEmbed.AddField("Queue Position", ActiveAudioClients[context.Guild.Id].Queue.Count);

        // Pass back
        return activeEmbed;
    }

    public void CloseFileStreams() // Closes FFMPEG, releasing file locks
    {
        _output.SetLength(0); // Close the FFMPEG output
    }

    public async Task DeleteNowPlayingMessage(SocketCommandContext context)
    {
        if (NowPlayingMessage != null)
        {
            await context.Channel.DeleteMessageAsync(NowPlayingMessage);
        }
    }

    public async Task PlayAudio(SocketCommandContext context)
    {
        // TODO: add checks if this used outside of the add song command
        // Find correct client
        var activeClient = ActiveAudioClients[context.Guild.Id];
        if (activeClient == null)
        {
            return;
        }

        var previousSkipStatus = false;
        // Check if already playing audio
        if (activeClient.Playing)
        {
            // exit no need
            return;
        }

        // Check queue status
        if (activeClient.Queue.Count == 0)
        {
            return;
        }

        // Set playing
        activeClient.Playing = true;
        // While there are songs to play
        while (activeClient.Playing)
        {
            var activeSong = activeClient.Queue.First();
            var youtubeClient = new YoutubeClient();
            var activeSongStream = await youtubeClient.Videos.Streams.GetAsync(activeSong.StreamInfo);

            // Get song from queue
            await Cli.Wrap("ffmpeg")
                .WithArguments(" -hide_banner -loglevel panic -i pipe:0 -f s16le -ac 2 -ar 48000 pipe:1")
                .WithStandardInputPipe(PipeSource.FromStream(activeSongStream))
                .WithStandardOutputPipe(PipeTarget.ToStream(_output))
                .ExecuteAsync();

            _output.Seek(0, SeekOrigin.Begin);

            // Create discord pcm stream
            await using var discord = activeClient.Client.CreatePCMStream(AudioApplication.Music);
            // Set speaking indicator
            await activeClient.Client.SetSpeakingAsync(true);
            // Send playing message
            // Modify embed
            var activeEmbed = activeClient.Queue.First.Value.EmbedBuilder;
            // Set playing title
            activeEmbed.WithTitle("Now Playing!");
            // Remove queue counter at the end of fields
            activeEmbed.Fields.RemoveAt(activeEmbed.Fields.Count - 1);
            //
            var spotifyQuery = await Helpers.SpotifyQuery(
                activeClient.Queue.First.Value.SearchTerm,
                activeClient.Queue.First.Value.MetaData.Title);

            // Match video to query to improve match

            if (spotifyQuery != null)
            {
                if (spotifyQuery.Count != 0)
                {
                    var spotifyHyperlink =
                        $" [Listen on Spotify](https://open.spotify.com/track/{spotifyQuery[0].Id})";

                    activeEmbed.AddField("Music Platforms", spotifyHyperlink);
                }
            }

            // Send
            NowPlayingMessage = await context.Channel.SendMessageAsync("", false, activeEmbed.Build());

            // Pin "Now-Playing" to the text channel
            // await NowPlayingMessage.PinAsync();

            // Delete the "Cecilia pinned a message..." message
            var messages = context.Channel.GetMessagesAsync(1).Flatten();

            await context.Channel.DeleteMessageAsync(messages.ToArrayAsync().Result[0].Id);

            // Stream and await till finish
            while (true)
            {
                // Stream is over, broken, or skip requested
                if (discord == null || activeClient.Skip)
                {
                    previousSkipStatus = activeClient.Skip;
                    CloseFileStreams();

                    break;
                }

                // Pause function while not playing
                if (activeClient.Paused)
                {
                    continue;
                }

                // Read a block of stream
                var blockSize = 2048;
                var buffer = new byte[blockSize];
                var byteCount = await _output.ReadAsync(buffer, 0, blockSize);

                // Stream cannot be read or file is ended
                if (byteCount <= 0)
                {
                    break;
                }

                // Write output to stream
                try
                {
                    await discord.WriteAsync(buffer, 0, byteCount);
                }
                catch (Exception e)
                {
                    // Flush buffer
                    await discord?.FlushAsync();
                    // Output exception
                    await Bot.CreateLogEntry(LogSeverity.Error, "MusicPlayer", e.ToString());
                    // Delete now-playing as it is now out of date
                    await DeleteNowPlayingMessage(context);

                    throw;
                }
            }

            // Delete now-playing as it is now out of date
            await DeleteNowPlayingMessage(context);

            // Flush buffer
            await discord?.ClearAsync(CancellationToken.None)!;
            CloseFileStreams();

            activeClient.Queue.RemoveFirst();

            // No more songs so exit
            if (activeClient.Queue.Count == 0)
            {
                activeClient.Playing = false;
            }

            // Reset skip trigger
            activeClient.Skip = false;
            await activeClient.Client.SetSpeakingAsync(false);
        }

        var response = Helpers.CeciliaEmbed(context);
        response.AddField("That's all folks!", "Spin up some more songs with the play command!");
        await context.Channel.SendMessageAsync("", false, response.Build());
    }

    public void RegisterAudioClient(ulong guildId, IAudioClient client, ulong channelId)
    {
        if (ActiveAudioClients.TryGetValue(guildId, out var audioClient))
        {
            return;
        }

        audioClient = new WrappedAudioClient(client)
        {
            ConnectedChannelId = channelId
        };

        ActiveAudioClients[guildId] = audioClient;

        Bot.CreateLogEntry(LogSeverity.Info, "Music Player", "Client Added");
    }

    public void RemoveAudioClient(ulong guildId)
    {
        ActiveAudioClients.Remove(guildId);

        Bot.CreateLogEntry(LogSeverity.Info, "Music Player", "Client Removed");
    }

    private static Process? CreateStream(string path) =>
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

    // Wraps the client with a queue and a mutex control for data access.
    public class WrappedAudioClient
    {
        // The raw client

        // Control over playing

        // Queue and a mutex for accessing

        public WrappedAudioClient(IAudioClient client)
        {
            Client = client;
            Queue = new LinkedList<QueueEntry>();
            Playing = false;
            Paused = false;
            Skip = false;
            QueueMutex = new Mutex();
            ConnectedChannelId = 0;
        }

        public IAudioClient Client { get; set; }

        public ulong ConnectedChannelId { get; set; }

        public bool Paused { get; set; }

        public bool Playing { get; set; }

        public LinkedList<QueueEntry> Queue { get; set; }

        public Mutex QueueMutex { get; set; }

        public bool Skip { get; set; }
    }

    public class QueueEntry
    {
        public QueueEntry(
            string searchTerm,
            Video metaData,
            IStreamInfo streamInfo,
            EmbedBuilder embedBuilder)
        {
            SearchTerm = searchTerm;
            MetaData = metaData;
            StreamInfo = streamInfo;
            EmbedBuilder = embedBuilder;
        }

        public EmbedBuilder EmbedBuilder { get; set; }
        public Video MetaData { get; set; }
        public string SearchTerm { get; set; }
        public IStreamInfo StreamInfo { get; set; }
    }
}