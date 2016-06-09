using System;
using System.Collections.Generic;
using System.Linq;
using Discord;

namespace Devkun
{
    static class Discord
    {
        /// <summary>
        /// Discord log
        /// </summary>
        private static Log mLog;


        /// <summary>
        /// Discord server that we operate on
        /// </summary>
        private static Server mServer;


        /// <summary>
        /// Discord channel that we operate on
        /// </summary>
        private static Channel mChannel;


        /// <summary>
        /// Our discord client
        /// </summary>
        private static DiscordClient mClient;


        /// <summary>
        /// Settings for our discord account
        /// </summary>
        private static AppSettings.DiscordSettings mSettings;


        /// <summary>
        /// List of all log files for each discord channel
        /// </summary>
        private static List<Log> mLogDiscord = new List<Log>();


        /// <summary>
        /// User token
        /// </summary>
        private static string mUserToken;


        /// <summary>
        /// State if we're connected
        /// </summary>
        public static bool mIsConnected;


        /// <summary>
        /// Connects to discord
        /// </summary>
        /// <param name="settings">Discord information settings</param>
        public static void Connect(AppSettings.DiscordSettings settings)
        {
            mLog = new Log("Discord", "Logs\\Discord\\Bot.txt", 1);
            mSettings = settings;
            DoWork();
        }


        /// <summary>
        /// Run the client
        /// </summary>
        public static async void DoWork()
        {
            mClient = new DiscordClient();
            mClient.Ready += MClient_Ready;
            mClient.MessageReceived += MClient_MessageReceived;
            mUserToken = await mClient.Connect(mSettings.email, mSettings.password);
        }


        /// <summary>
        /// Sends a message to primary channel
        /// </summary>
        /// <param name="message">Message to send</param>
        /// <returns>Returns true if sent</returns>
        public static bool SendMessage(string message)
        {
            try
            {
                if (!mIsConnected)
                {
                    mLog.Write(Log.LogLevel.Error, "Not connected to Discord...");
                    return false;
                }

                mChannel.SendMessage(message);
                return true;
            }
            catch (Exception ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Error sending Discord message ({message}) - {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// When a message is posted to Discord
        /// </summary>
        private static void MClient_MessageReceived(object sender, MessageEventArgs e)
        {
            if (e.Server == mServer)
            {
                Log log = mLogDiscord.Where(o => o.mLogName == e.Channel.Name).FirstOrDefault();
                if (e.User.Id == mClient.CurrentUser.Id)
                {
                    mLog.Write(Log.LogLevel.Debug, e.Message.Text, true, false);
                }
                else
                {
                    if (log == null)
                        return;

                    log.Write(Log.LogLevel.Debug, $"({e.Channel.Name}) ({e.User.Name}) : {e.Message.Text}", true, false);
                }
            }
        }


        /// <summary>
        /// Finds and sets the discord server and channel on which we should operate on
        /// </summary>
        private static void SetServerAndChannel()
        {
            /*Get server*/
            mServer = mClient.FindServers(mSettings.serverName).FirstOrDefault();
            if (mServer == null)
            {
                mLog.Write(Log.LogLevel.Error, $"Could not find server {mSettings.serverName}");
                return;
            }

            /*Get channel*/
            mChannel = mServer.FindChannels(mSettings.channelName).FirstOrDefault(o => o.Type == "text");
            if (mChannel == null)
            {
                mLog.Write(Log.LogLevel.Error, $"Could not find channel {mSettings.channelName}");
                return;
            }
        }


        /// <summary>
        /// Create a log for each channel
        /// </summary>
        private static void SetChannelLogs()
        {
            foreach (var channel in mServer.AllChannels.Where(o => o.Type == "text"))
            {
                Log log = new Log(channel.Name, $"Logs\\Discord\\Channels\\{channel.Name}.txt", 3);
                mLogDiscord.Add(log);
            }
        }


        /// <summary>
        /// Executes when client is ready
        /// </summary>
        private static void MClient_Ready(object sender, EventArgs e)
        {
            SetServerAndChannel();
            if (mServer == null || mChannel == null)
                mLog.Write(Log.LogLevel.Error, "Discord error. Server or Channel is null.");

            SetChannelLogs();
            mIsConnected = true;
            SendMessage("Connected to Discord");
            mLog.Write(Log.LogLevel.Success, "Connected to Discord");
        }
    }
}
