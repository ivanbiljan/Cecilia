using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cecilia_NET.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SpotifyAPI.Web;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Cecilia_NET;

// Collection of functions that don't fit into class
public static class Helpers
{
    // THIS COMES FROM THE DISCORD API
    public const int MAX_FIELD_IN_EMBED = 25;

    // Provide a shell embed builder with cecilia branding and requesting user
    // Adds the author and footer
    public static EmbedBuilder CeciliaEmbed(SocketCommandContext context)
    {
        var outBuilder = new EmbedBuilder();
        outBuilder.WithAuthor(context.Client.CurrentUser.Username, context.Client.CurrentUser.GetAvatarUrl());
        var correctedMinutes = DateTime.Now.Minute <= 9 ? $"0{DateTime.Now.Minute}" : DateTime.Now.Minute.ToString();
        outBuilder.WithFooter($"Requested by {GetDisplayName(context.User)} @ {DateTime.Now.Hour}:{correctedMinutes}");

        return outBuilder;
    }

    public static async Task DownloadSong(IStreamInfo streamInfo, string filePath)
    {
        var youtube = new YoutubeClient();
        await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath);
    }

    // Add a 0 before numbers number 9
    public static string FixTime(int time) => time <= 9 ? $"0{time}" : time.ToString();

    // Gets a display name. Username if the user doesnt have a nickname in the guild they are running the command in. Else nickname
    public static string GetDisplayName(SocketUser user)
    {
        var guildUser = user as SocketGuildUser;
        if (guildUser == null)
        {
            return user.Username;
        }

        return guildUser.Nickname ?? guildUser.Username;
    }

    public static bool IsChannelValid(SocketCommandContext ctx, MusicPlayer player)
    {
        // Check if bot is in a channel
        if (player.ActiveAudioClients[ctx.Guild.Id].Client.ConnectionState == ConnectionState.Disconnected)
        {
            return false;
        }

        // Check if they are in a channel
        var guildUser = (SocketGuildUser)ctx.User;
        if (guildUser.VoiceChannel == null)
        {
            return false;
        }

        // They have to in the bots channel
        return guildUser.VoiceChannel.Id == player.ActiveAudioClients[ctx.Guild.Id].ConnectedChannelId;
    }

    // Delete the message that sent the command

    // Remove characters that could break filenames & paths
    public static string ProcessVideoTitle(string videoTitle)
    {
        const char replacementChar = '-';

        // Remove forward slashes
        var output = videoTitle.Replace('/', replacementChar);
        // remove back slashes
        output = output.Replace('\\', replacementChar);
        // Remove all colons
        output = output.Replace(':', replacementChar);
        // Remove all asterisks
        output = output.Replace('*', replacementChar);
        // Remove all question marks
        output = output.Replace('?', replacementChar);
        // Remove all double quotes
        output = output.Replace('\"', replacementChar);
        // Remove all left chevrons
        output = output.Replace('<', replacementChar);
        // Remove all right chevrons
        output = output.Replace('>', replacementChar);
        // Remove all left graves
        output = output.Replace('|', replacementChar);

        return output;
    }

    public static async Task<List<FullTrack>> SpotifyQuery(string searchTerm, string videoTitle)
    {
        // This is not perfect but should help with things like BAND - SONG (live in yada yada)

        var newSearchTerm = "";

        if (searchTerm.Contains("http"))
        {
            newSearchTerm = videoTitle;
        }
        else
        {
            newSearchTerm = searchTerm;
        }

        if (newSearchTerm.Contains('('))
        {
            newSearchTerm = newSearchTerm.Remove(newSearchTerm.IndexOf('('));
        }

        if (newSearchTerm.Contains('['))
        {
            newSearchTerm = newSearchTerm.Remove(newSearchTerm.IndexOf('['));
        }

        newSearchTerm = newSearchTerm.Replace('/', ' ');

        newSearchTerm = newSearchTerm.Replace("  ", " ");

        // If they haven't provided a client then leave
        if (Bot.SpotifyConfig == null)
        {
            return null;
        }

        var spotify = new SpotifyClient(Bot.SpotifyConfig);
        SearchResponse search;
        try
        {
            search = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, newSearchTerm));
        }
        catch (Exception e)
        {
            await Bot.CreateLogEntry(LogSeverity.Warning, "Spotify", e.Message);

            return null;
        }

        if (search.Tracks.Items?.Count == 0)
        {
            return null;
        }

        return search.Tracks.Items;
    }
}