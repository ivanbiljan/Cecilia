using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace Cecilia_NET.Modules;

// Collection of simple commands such as ping, uptime etc
public class SimpleCommandsModule : ModuleBase<SocketCommandContext>
{
    private readonly CommandService _commandService;

    public SimpleCommandsModule(CommandService commandService)
    {
        _commandService = commandService;
    }

    [Command("Help")]
    public async Task Help()
    {
        List<CommandInfo> commands = _commandService.Commands.ToList();
        var embedBuilder = Helpers.CeciliaEmbed(Context);

        embedBuilder.WithTitle("Command List");
        // Remove footer for dm
        embedBuilder.WithFooter("");
        foreach (var command in commands)
        {
            // Get the command Summary attribute information
            var embedFieldText = command.Summary ?? "No description available\n";

            embedBuilder.AddField(command.Name, embedFieldText);
        }

        await Context.User.SendMessageAsync("", false, embedBuilder.Build());

        var newEmbed = Helpers.CeciliaEmbed(Context);

        newEmbed.AddField("Check your DM's!", "I sent you all my commands!");

        await Context.Channel.SendMessageAsync("", false, newEmbed.Build());
    }

    [Command("Ping")]
    [Summary("Pings a message back. Allows checking of health")]
    public async Task PingAsync()
    {
        var response = Helpers.CeciliaEmbed(Context);
        response.AddField("Pong!", "Im here, thanks for checking on me!");
        await Context.Channel.SendMessageAsync("", false, response.Build());
    }

    [Command("WhoIs")]
    [Summary("Sends an embed with data on the attached user")]
    public async Task WhoisAsync([Summary("The user to query")] SocketUser user)
    {
        // Delete sending message

        // Cast to guild specific data
        if (!(user is SocketGuildUser guildUser))
        {
            return;
        }

        // Prepare to build embed
        var embedBuilder = new EmbedBuilder();

        // Set title and image
        // Title is the users username
        embedBuilder.WithTitle(guildUser.Username);
        // Image is profile pic
        embedBuilder.WithImageUrl(guildUser.GetAvatarUrl());
        // Output nickname if they have one
        if (Helpers.GetDisplayName(guildUser) != guildUser.Username)
        {
            embedBuilder.AddField("Nickname", Helpers.GetDisplayName(guildUser));
        }

        // when they joined
        if (guildUser.JoinedAt.HasValue)
        {
            var joinTime = guildUser.JoinedAt.Value;
            embedBuilder.AddField(
                "Joined",
                $"{joinTime.Day}/{joinTime.Month}/{joinTime.Year} @ {joinTime.Hour}:{Helpers.FixTime(joinTime.Minute)}:{Helpers.FixTime(joinTime.Second)}");
        }

        // Footer with requester
        embedBuilder.WithFooter($"Requested by {Helpers.GetDisplayName(Context.User)}");

        await Context.Channel.SendMessageAsync("", false, embedBuilder.Build());
    }
}