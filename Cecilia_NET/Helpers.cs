using Discord.WebSocket;

namespace Cecilia_NET
{
    // Collection of functions that don't fit into class
    public static class Helpers
    {
        // Gets a display name. Username if the user doesnt have a nickname in the guild they are running the command in. Else nickname
        public static string GetDisplayName(SocketUser user)
        {
            var guildUser = user as SocketGuildUser;
            if (guildUser == null)
            {
                return user.Username;
            }
            else
            {
                return guildUser.Nickname ?? guildUser.Username;
            }
        }

        public static string ProcessVideoTitle(string videoTitle)
        {
            // Remove forward slashes
            var output = videoTitle.Replace('/', ' ');
            // remove back slashes
            output = output.Replace('\\',' ');
            // Remove all spaces
            output = output.Replace(' ', '-');

            return output;
        }
    }
}