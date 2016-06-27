using Discord;
using Discord.Commands;
using Discord.Commands.Permissions;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;

namespace Devkun
{
    class Discord
    {
        /// <summary>
        /// Discord settings
        /// </summary>
        private AppSettings.DiscordSettings mSettings;


        /// <summary>
        /// Main backgroundworker
        /// </summary>
        private BackgroundWorker mBackgroundWorker;

        
        /// <summary>
        /// Our discord client
        /// </summary>
        private DiscordClient mClient;


        /// <summary>
        /// Discord class log
        /// </summary>
        private Log mLog, mLogMessages;


        /// <summary>
        /// Main session for callbacks
        /// </summary>
        private Session mSession;


        /// <summary>
        /// If discord is connected
        /// </summary>
        private bool mConnected;


        /// <summary>
        /// Enum permission level of a user in discord
        /// </summary>
        public enum PermissionLevel
        {
            Admin,
            User
        }


        /// <summary>
        /// Enum commads available
        /// </summary>
        public enum CommandList
        {
            Help,
            Codes,
            Restart,
            Status,
            Offers,
            RemoveOffer,
            Pause,
            PauseAll,
            Unpause,
            Clear
        }


        /// <summary>
        /// Class constructor
        /// We'll load up the client here and make it ready for sending messages
        /// </summary>
        public Discord(Session session, AppSettings.DiscordSettings settings)
        {
            mLog = new Log("Discord", "Logs\\Discord\\Session.txt", 3);
            mLogMessages = new Log("Discord Messages", "Logs\\Discord\\Messages.txt", 3);

            mSession = session;
            mSettings = settings;

            mClient = new DiscordClient(x =>
            {
                x.AppName = "CSGO-Raffle";
                x.AppUrl = "http://www.csgo-raffle.com";
                x.AppVersion = "1.0";
                x.MessageCacheSize = 0;
                x.UsePermissionsCache = true;
                x.EnablePreUpdateEvents = true;
            });

            mClient.MessageReceived += MClient_MessageReceived;
            mClient.MessageUpdated += MClient_MessageUpdated;
            mClient.Ready += MClient_Ready;

            mBackgroundWorker = new BackgroundWorker();
            mBackgroundWorker.WorkerSupportsCancellation = true;
            mBackgroundWorker.DoWork += MBackgroundWorker_DoWork;
            mBackgroundWorker.RunWorkerAsync();

            while (!mConnected)
                Thread.Sleep(250);
        }


        /// <summary>
        /// Discord background worker
        /// We'll host the bot thread on here
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">DoWorkEventArgs</param>
        private void MBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            mClient.ExecuteAndWait(async () =>
            {
                while (true)
                {
                    try
                    {
                        await mClient.Connect(mSettings.accessToken);
                        mClient.SetGame(mSettings.gameName);
                        break;
                    }
                    catch (Exception ex)
                    {
                        mLog.Write(Log.LogLevel.Error, $"Discord login failed: {ex}");
                        await Task.Delay(mClient.Config.FailedReconnectDelay);
                    }
                }
            });
        }


        /// <summary>
        /// Discord bot ready
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">EventArgs</param>
        private void MClient_Ready(object sender, EventArgs e)
        {
            mLog.Write(Log.LogLevel.Info, $"Discord has connected!");

            mClient.CurrentUser.Edit("", mSettings.displayName);
            mClient.SetGame(mSettings.gameName);

            RegisterCommands();
            mConnected = true;
        }


        /// <summary>
        /// Discord message updated
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">MessageUpdatedEventArgs</param>
        private void MClient_MessageUpdated(object sender, MessageUpdatedEventArgs e)
        {
            mLogMessages.Write(Log.LogLevel.Text, 
                $"{e.User.Name} @ {e.Channel.Name} updated: {e.After.Text}");
        }


        /// <summary>
        /// Discord message received
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">MessageEventArgs</param>
        private void MClient_MessageReceived(object sender, MessageEventArgs e)
        {
            mLogMessages.Write(Log.LogLevel.Text, 
                $"{e.User.Name} @ {e.Channel.Name}: {e.Message.Text}");

            if (e.Channel.IsPrivate)
                e.User.SendMessage("I don't work in private messages.");
        }


        /// <summary>
        /// Register all the commands that we'll use
        /// </summary>
        private void RegisterCommands()
        {
            var comService = new CommandService(
                new CommandServiceConfigBuilder
            {
                AllowMentionPrefix = false,
                PrefixChar = '!',
                HelpMode = HelpMode.Disabled
            });
            
            comService.CreateCommand("help")
                .AddCheck(PermissionChecker.Admin)
                .Description("Posts all the available commands")
                .Do(e => { mSession.OnDiscordHelp(e); });
            
            comService.CreateCommand("codes")
                .AddCheck(PermissionChecker.Admin)
                .Description("Messages all the access codes for the running bots.")
                .Do(e => { mSession.OnDiscordCodes(e); });
            
            comService.CreateCommand("restart")
                .AddCheck(PermissionChecker.Admin)
                .Description("Pauses all actions and restarts bots")
                .Do(e => { mSession.OnDiscordRestart(e); });
            
            comService.CreateCommand("status")
                .AddCheck(PermissionChecker.Admin)
                .Description("Posts session information")
                .Do(e => { mSession.OnDiscordStatus(e); });
            
            comService.CreateCommand("offers")
                .AddCheck(PermissionChecker.Admin)
                .Description("Posts all active offers")
                .Do(e => { mSession.OnDiscordGetOffers(e); });
            
            comService.CreateCommand("removeoffer")
                .AddCheck(PermissionChecker.Admin)
                .Description("Remove trade offer from list")
                .Parameter("queueid", ParameterType.Required)
                .Do(e => { mSession.OnDiscordRemoveOffer(e); });
            
            comService.CreateCommand("pause")
                .AddCheck(PermissionChecker.Admin)
                .Description("Pauses deposits & withdraws")
                .Do(e => { mSession.OnDiscordPause(e); });
            
            comService.CreateCommand("pauseall")
                .AddCheck(PermissionChecker.Admin)
                .Description("Pauses deposits & withdraws, but also active offers")
                .Do(e => { mSession.OnDiscordPauseAll(e); });
            
            comService.CreateCommand("unpause")
                .AddCheck(PermissionChecker.Admin)
                .Description("Unpauses any pause status")
                .Do(e => { mSession.OnDiscordUnpause(e); });
            
            comService.CreateCommand("clear")
                .AddCheck(PermissionChecker.Admin)
                .Description("Clears all active trade offers")
                .Do(e => { mSession.OnDiscordClear(e); });

            mClient.AddService(comService);
        }


        /// <summary>
        /// Posts message to channel
        /// </summary>
        /// <param name="msg">String to post</param>
        public void PostMessage(string msg, Channel channel = null)
        {
            Channel chan;

            /*If the provided Channel is null then we'll fetch our main channel
            which has been specified in the application settings json file*/
            if (channel == null)
                chan = mClient.GetChannel(mSettings.mainChannelId);
            else
                chan = channel;

            if (chan != null)
                chan.SendMessage(msg);
        }
    }


    /// <summary>
    /// Discord permission interface class
    /// </summary>
    internal class PermissionChecker : IPermissionChecker
    {
        /// <summary>
        /// Permission instance
        /// </summary>
        public static PermissionChecker Admin { get; } = new PermissionChecker();
        

        /// <summary>
        /// If command can run
        /// </summary>
        /// <param name="command">Command</param>
        /// <param name="user">User</param>
        /// <param name="channel">Channel</param>
        /// <param name="error">Out error</param>
        /// <returns>Returns true if user is admin</returns>
        public bool CanRun(Command command, User user, Channel channel, out string error)
        {
            error = string.Empty;
            if (channel.IsPrivate || channel.Server == null)
                return false;

            /*We'll consider the user to be of adminstrative
            powers if they can ban users from the server*/
            return user.ServerPermissions.BanMembers;
        }
    }
}
