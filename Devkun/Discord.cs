using Discord;
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
        /// If discord is connected
        /// </summary>
        private bool mConnected;


        /// <summary>
        /// Represents the permission level of a user in discord
        /// </summary>
        enum PermissionLevel
        {
            Admin,
            User
        }


        /// <summary>
        /// Class constructor
        /// We'll load up the client here and make it ready for sending messages
        /// </summary>
        public Discord(AppSettings.DiscordSettings settings)
        {
            mLog = new Log("Discord", "Logs\\Discord\\Session.txt", 3);
            mLogMessages = new Log("Discord Messages", "Logs\\Discord\\Messages.txt", 3);
            mSettings = settings;

            mClient = new DiscordClient(x =>
            {
                x.AppName = "CSGO-Raffle";
                x.AppUrl = "http://www.csgo-raffle.com";
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
                        await mClient.Connect(mSettings.token);
                        mClient.SetGame(mSettings.displayGame);
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
                $"({PermissionResolver(e.User, e.Channel)}){e.User.Name} @ {e.Channel.Name} updated: {e.After.Text}");
        }


        /// <summary>
        /// Discord message received
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">MessageEventArgs</param>
        private void MClient_MessageReceived(object sender, MessageEventArgs e)
        {
            mLogMessages.Write(Log.LogLevel.Text, 
                $"({PermissionResolver(e.User, e.Channel)}){e.User.Name} @ {e.Channel.Name}: {e.Message.Text}");
        }


        /// <summary>
        /// Resolve discord rank for user
        /// </summary>
        /// <param name="user">User</param>
        /// <param name="channel">Channel</param>
        /// <returns>Returns permissionlevel</returns>
        private PermissionLevel PermissionResolver(User user, Channel channel)
        {
            if (user.Server != null && user.ServerPermissions.BanMembers)
                return PermissionLevel.Admin;

            return PermissionLevel.User;
        }


        /// <summary>
        /// Posts message to channel
        /// </summary>
        /// <param name="msg">String to post</param>
        public void PostMessage(string msg)
        {
            Channel chan = mClient.GetChannel(mSettings.channelId);

            if (chan != null)
                chan.SendMessage(msg);
        }
    }
}
