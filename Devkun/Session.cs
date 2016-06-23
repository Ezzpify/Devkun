using System;
using System.Collections.Generic;
using Discord.Commands;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SteamTrade.TradeOffer;
using System.ComponentModel;
using System.Diagnostics;
using System.Timers;
using SteamTrade;

namespace Devkun
{
    class Session
    {
        /// <summary>
        /// List of active trades queue
        /// These items will be added to mActiveTradesList
        /// </summary>
        private List<Config.TradeObject> mActiveTradesListQueue { get; set; } = new List<Config.TradeObject>();


        /// <summary>
        /// List of active trades
        /// </summary>
        private List<Config.TradeObject> mActiveTradesList { get; set; } = new List<Config.TradeObject>();


        /// <summary>
        /// Timer that will restart the bots once a day
        /// </summary>
        private System.Timers.Timer mScheduledRestartTimer { get; set; }


        /// <summary>
        /// Application global settings
        /// </summary>
        private AppSettings.ApplicationSettings mSettings { get; set; }


        /// <summary>
        /// List of all bots
        /// </summary>
        private List<Bot> mBotList { get; set; } = new List<Bot>();


        /// <summary>
        /// Main background worker to get the trades
        /// And add them to the queue
        /// </summary>
        private BackgroundWorker mTradeWorker { get; set; }


        /// <summary>
        /// Main background worker to deal with trade queues
        /// </summary>
        private BackgroundWorker mQueueWorker { get; set; }


        /// <summary>
        /// Our item database
        /// </summary>
        private Database mDatabase { get; set; }


        /// <summary>
        /// Discord client connection
        /// </summary>
        private Discord mDiscord { get; set; }


        /// <summary>
        /// Host bot used for trading with users
        /// </summary>
        private Bot mBotHost { get; set; }


        /// <summary>
        /// Enum representing the session state
        /// </summary>
        private enum SessionState
        {
            /// <summary>
            /// Everything is active
            /// </summary>
            Active,


            /// <summary>
            /// Incoming trades will be paused
            /// Existing trades will still be processed
            /// </summary>
            Paused,


            /// <summary>
            /// All actions are paused
            /// Nothing will work. NOTHING!
            /// </summary>
            Locked
        }


        /// <summary>
        /// Session state variable
        /// </summary>
        private SessionState mSessionState;
        

        /// <summary>
        /// Session log
        /// </summary>
        private Log mLog;


        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="settings">Application settings class</param>
        public Session(AppSettings.ApplicationSettings settings)
        {
            mSettings = settings;
            mLog = new Log("Session", "Logs\\Session.txt", 3);

            mDatabase = new Database("Database\\Items.sqlite");
            if (!mDatabase.mConnected)
                return;

            if (AddBots(settings.bots))
            {
                /*Find first host bot in our list and assign it as host*/
                mBotHost = mBotList.FirstOrDefault(o => o.mBotType == Bot.BotType.Main);
                if (mBotHost == null)
                {
                    mLog.Write(Log.LogLevel.Error, "Could not find a main bot from the list. We cannot continue.");
                    return;
                }

                /*Start trade worker thread, this will send items to users*/
                mTradeWorker = new BackgroundWorker { WorkerSupportsCancellation = true };
                mTradeWorker.DoWork += TradeWorkerOnDoWork;
                mTradeWorker.RunWorkerCompleted += TradeWorkerOnRunWorkerCompleted;
                mTradeWorker.RunWorkerAsync();

                /*Starts queuue worker thread, this will go through send offers and check their state*/
                mQueueWorker = new BackgroundWorker { WorkerSupportsCancellation = true };
                mQueueWorker.DoWork += MQueueWorker_DoWork;
                mQueueWorker.RunWorkerCompleted += MQueueWorkerOnRunWorkerCompleted;
                mQueueWorker.RunWorkerAsync();

                /*Start the daily scheduled restart timer*/
                mScheduledRestartTimer = new System.Timers.Timer();
                mScheduledRestartTimer.Interval = (new TimeSpan(08, 00, 00) - DateTime.Now.TimeOfDay).TotalMilliseconds;
                mScheduledRestartTimer.Elapsed += new ElapsedEventHandler(ScheduledDailyRestart);
                mScheduledRestartTimer.Start();

                /*Connect to Discord*/
                mDiscord = new Discord(this, settings.discord);

                /*Clear console of all the junk*/
                mLog.Write(Log.LogLevel.Info, $"Finished loading bots. Clearing console...", false);
                Thread.Sleep(1000);
                Console.Clear();

                /*We'll pause here until either thread breaks*/
                /*It will go be on a loop until either of the threads die*/
                while (mTradeWorker.IsBusy && mQueueWorker.IsBusy)
                    Thread.Sleep(1000);

                /*Log information about which worker that isn't running*/
                mLog.Write(Log.LogLevel.Info,
                $"A worker has exited."
                + $"mTradeWorker running status: {mTradeWorker.IsBusy}"
                + $"mQueueWorker running status: {mQueueWorker.IsBusy}");

                /*Send cancel request to our threads*/
                mTradeWorker.CancelAsync();
                mQueueWorker.CancelAsync();
            }
            else
            {
                mLog.Write(Log.LogLevel.Error, "Bots were not added");
            }
        }


        /// <summary>
        /// Initializes all the bots from settings
        /// </summary>
        /// <param name="botList">List of bots</param>
        private bool AddBots(List<AppSettings.BotSettings> botList)
        {
            /*Verify all bots in settings and add them to global got list*/
            foreach (var bot in botList)
            {
                /*First we want to make sure the bot is not missing any properties*/
                if (bot.HasEmptyProperties())
                {
                    /*If any bot fails we want to return false*/
                    mLog.Write(Log.LogLevel.Error, $"BOT {bot.displayName} is lacking properties.");
                    return false;
                }

                /*Add active bot to list*/
                mBotList.Add(new Bot(bot));
            }

            /*Start all the bots*/
            mBotList.ForEach(o => o.Connect(true));
            return mBotList.Count > 0;
        }


        /// <summary>
        /// Get list of tradeobjects from website
        /// </summary>
        /// <returns>Returns list of Config.TradeObject</returns>
        private List<Config.TradeObject> GetQueue()
        {
            /*Get pending trades from the website*/
            string tradeJson = Website.GetTrades();
            var tradeList = new List<Config.TradeObject>();

            try
            {
                if (string.IsNullOrWhiteSpace(tradeJson))
                {
                    mLog.Write(Log.LogLevel.Warn, $"Website response in TradeWorkerOnDoWork was empty? Resp: {tradeJson}");
                }
                else
                {
                    /*Attempt to deserialize the response we got here*/
                    var trade = JsonConvert.DeserializeObject<Config.Trade>(tradeJson);

                    /*Go through all deposit objects*/
                    foreach (var deposit in trade.Deposits)
                    {
                        deposit.tradeType = Config.TradeType.Deposit;
                        deposit.tradeStatus = new Config.TradeStatus() { Id = deposit.QueId, SteamId = deposit.SteamId.ToString(), Status = Config.TradeStatusType.DepositPending};

                        foreach (var itemStr in deposit.item_Ids)
                        {
                            /*Split the item string and assign a real item*/
                            var item = Functions.SplitItem(itemStr);
                            item.BotOwner = mBotHost.GetBotSteamId64();
                            deposit.Items.Add(item);
                        }

                        tradeList.Add(deposit);
                    }

                    /*Go through all withdraw objects*/
                    foreach (var withdraw in trade.withdrawal)
                    {
                        withdraw.tradeType = Config.TradeType.Withdraw;
                        withdraw.tradeStatus = new Config.TradeStatus() { Id = withdraw.QueId, SteamId = withdraw.SteamId.ToString(), Status = Config.TradeStatusType.WithdrawPending };

                        foreach (var itemStr in withdraw.item_Ids)
                        {
                            /*Split the item string and assign a real item*/
                            var item = Functions.SplitItem(itemStr);
                            item.BotOwner = mBotHost.GetBotSteamId64();
                            withdraw.Items.Add(item);
                        }

                        tradeList.Add(withdraw);
                    }
                }
            }
            catch (FormatException ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Format exception occured when trying to parse website trade json. Message: {ex.Message}.");
                mLog.Write(Log.LogLevel.Error, $"Website response: {tradeJson}", true, false);
            }
            catch (JsonSerializationException ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Serialization exception occured when trying to parse website trade json. Message: {ex.Message}");
                mLog.Write(Log.LogLevel.Error, $"Website response: {tradeJson}", true, false);
            }
            catch (Exception ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Exception occured when trying to parse website trade json. Message: {ex.Message}");
                mLog.Write(Log.LogLevel.Error, $"Website response: {tradeJson}", true, false);
            }

            Website.UpdateTrade(tradeList);
            return tradeList;
        }


        /// <summary>
        /// Checks if a user has an escrow waiting period
        /// </summary>
        /// <param name="trade">Trade to check</param>
        /// <returns>Returns true if it's ok to trade</returns>
        private bool IsUserEscrowReady(Config.TradeObject trade)
        {
            int daysTheirEscrow = mBotHost.GetUserEscrowWaitingPeriod(trade);
            if (daysTheirEscrow > 0)
            {
                /*User has an escrow waiting period, which means we can't sent the trade to them*/
                mLog.Write(Log.LogLevel.Error, $"User {trade.SteamId} has an escrow waiting period of {daysTheirEscrow} days. Will not continue.");
                trade.tradeStatus.Status = trade.tradeType == Config.TradeType.Deposit ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;

                /*123 is the default number that means something went wrong with the request*/
                if (daysTheirEscrow == 123)
                    mLog.Write(Log.LogLevel.Info, $"123 days means that it failed to check how many days the user has left.");

                return false;
            }

            return true;
        }


        /// <summary>
        /// Main trade background worker
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="doWorkEventArgs"></param>
        private void TradeWorkerOnDoWork(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            while (!mTradeWorker.CancellationPending)
            {
                /*If we should pause our actions*/
                while (mSessionState == SessionState.Paused || mSessionState == SessionState.Locked)
                    Thread.Sleep(500);

                /*Get trade queue from website*/
                var tradeList = GetQueue();
                foreach (var trade in tradeList)
                {
                    mLog.Write(Log.LogLevel.Debug, $"Examining {trade.tradeType} from {trade.SteamId}");
                    if (IsUserEscrowReady(trade))
                    {
                        switch (trade.tradeType)
                        {
                            case Config.TradeType.Deposit:
                                {
                                    /*Send a deposit trade to user*/
                                    mLog.Write(Log.LogLevel.Info, $"Sending {trade.SteamId} to Deposits");
                                    trade.tradeStatus = HandleDeposit(trade);
                                }
                                break;
                            case Config.TradeType.Withdraw:
                                {
                                    /*Send a withdraw trade to user*/
                                    mLog.Write(Log.LogLevel.Info, $"Sending {trade.SteamId} to Withdraws");
                                    trade.tradeStatus = HandleWithdraw(trade);
                                }
                                break;
                        }
                    }
                }

                /*Authenticate trades*/
                Website.UpdateTrade(tradeList);
                mBotHost.ConfirmTrades();
                Thread.Sleep(5000);
            }
        }


        /// <summary>
        /// Deal with deposit offers
        /// </summary>
        /// <param name="trade">TradeObject</param>
        private Config.TradeStatus HandleDeposit(Config.TradeObject trade)
        {
            /*Attempt to send the trade offer to user*/
            mLog.Write(Log.LogLevel.Info, $"Attempting to send deposit trade offer to {trade.SteamId}");
            string offerId = mBotHost.SendTradeOffer(trade, Config.TradeType.Deposit, $"{EndPoints.Website.DOMAIN.ToUpper()} DEPOSIT | {trade.SecurityToken}");

            if (string.IsNullOrWhiteSpace(offerId))
            {
                /*Trade offer id was empty, so that means the offer failed to send*/
                mLog.Write(Log.LogLevel.Error, $"Deposit trade offer was not sent to user {trade.SteamId}");
                trade.tradeStatus.Status = Config.TradeStatusType.DepositDeclined;
            }
            else
            {
                /*Offer was sent successfully*/
                mLog.Write(Log.LogLevel.Success, $"Deposit trade offer was sent to user {trade.SteamId}");
                trade.tradeStatus.Status = Config.TradeStatusType.DepositSent;
                trade.tradeStatus.Tradelink = offerId;
                mActiveTradesListQueue.Add(trade);
            }

            return trade.tradeStatus;
        }


        /// <summary>
        /// Deal with withdraw offers
        /// </summary>
        /// <param name="trade">TradeObject</param>
        private Config.TradeStatus HandleWithdraw(Config.TradeObject trade)
        {
            /*Get all items from database*/
            var itemList = new List<Config.Item>();
            foreach (var item in trade.Items.GroupBy(o => o.ClassId).Select(q => q.First()))
                itemList.AddRange(mDatabase.FindEntry(Database.DBCols.ClassId, item.ClassId.ToString()));

            /*Sort them so we'll best the best possible bots*/
            itemList = Functions.SortDBItems(itemList, trade.Items);
            mLog.Write(Log.LogLevel.Info, $"Found {itemList.Count} perfect items. Requested count: {trade.Items.Count}");

            if (itemList.Count < trade.Items.Count)
                mLog.Write(Log.LogLevel.Error, $"We found less items than what was requested");

            /*Get the new itemids for each item*/
            trade.Items = FindUpdatedItems(mBotHost.GetInventory(), itemList);

            /*Send the trade offer*/
            mLog.Write(Log.LogLevel.Info, $"Attempting to send withdraw trade offer to {trade.SteamId}");
            string offerId = mBotHost.SendTradeOffer(trade, Config.TradeType.Withdraw, $"{EndPoints.Website.DOMAIN.ToUpper()} DEPOSIT | {trade.SecurityToken}");

            if (string.IsNullOrWhiteSpace(offerId))
            {
                /*Trade offer id was empty, so that means the offer failed to send*/
                mLog.Write(Log.LogLevel.Error, $"Withdraw trade offer was not sent to user {trade.SteamId}");
                trade.tradeStatus.Status = Config.TradeStatusType.WithdrawDeclined;
            }
            else
            {
                /*Offer was sent successfully, update the trade status*/
                mLog.Write(Log.LogLevel.Success, $"Withdraw trade offer was sent to user {trade.SteamId}");
                trade.tradeStatus.Status = Config.TradeStatusType.WithdrawSent;
                trade.tradeStatus.Tradelink = offerId;

                /*Add items sent to used items in the database*/
                var assetList = trade.Items.Select(o => o.AssetId).ToList();
                mDatabase.InsertUsedItems(assetList);

                /*Set state to sent which means that next withdraw won't pick the same items*/
                var idList = trade.Items.Select(o => o.ID).ToList();
                mDatabase.UpdateItems(idList, Database.ItemState.Sent);
                mLog.Write(Log.LogLevel.Info, $"{idList.Count} items updated in the database");
                mActiveTradesListQueue.Add(trade);
            }

            return trade.tradeStatus;
        }

        
        /// <summary>
        /// Checks active trade list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MQueueWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (!mQueueWorker.CancellationPending)
            {
                /*If we should pause our actions*/
                while (mSessionState == SessionState.Locked)
                    Thread.Sleep(500);

                /*Add all trades from queue list to main list*/
                if (mActiveTradesListQueue.Count > 0)
                {
                    mActiveTradesList.AddRange(mActiveTradesListQueue);
                    mActiveTradesListQueue.Clear();
                }

                /*List of items that will be deleted from main list when this function has run*/
                var deleteList = new List<Config.TradeObject>();

                /*Go through the active trades list*/
                foreach (var trade in mActiveTradesList)
                {
                    /*Try to get the trade offer state*/
                    mLog.Write(Log.LogLevel.Info, $"Checking active {trade.tradeType} trade offer to user {trade.SteamId}");
                    
                    /*Get updated trade offer from api*/
                    TradeOffer offer = mBotHost.GetTradeOffer(trade.tradeStatus.Tradelink);
                    if (offer == null)
                    {
                        mLog.Write(Log.LogLevel.Warn, $"Trade offer returned null");
                        trade.errorCount++;
                        continue;
                    }

                    /*Get the state of the offer*/
                    mLog.Write(Log.LogLevel.Info, $"Trade offer status: {offer.OfferState}");
                    trade.offerState = offer.OfferState;

                    if (offer.OfferState == TradeOfferState.TradeOfferStateAccepted)
                    {
                        /*Trade offer was accepted, so add the items to the database*/
                        deleteList.Add(trade);
                        trade.tradeStatus.Status = (trade.tradeType == Config.TradeType.Deposit) ? Config.TradeStatusType.DepositAccepted : Config.TradeStatusType.WithdrawAccepted;

                        if (trade.tradeType == Config.TradeType.Deposit)
                        {
                            /*The trade was a deposit, so we'll enter the items to the database*/
                            mDatabase.InsertItems(trade.Items);
                            mLog.Write(Log.LogLevel.Info, $"Items added to database");
                            trade.tradeStatus.Status = Config.TradeStatusType.DepositAccepted;
                        }
                        else
                        {
                            /*Set state to accepted which is final stage*/
                            /*These items will be moved to back-up database*/
                            var idList = trade.Items.Select(o => o.ID).ToList();
                            mDatabase.UpdateItems(idList, Database.ItemState.Accepted);
                            mLog.Write(Log.LogLevel.Info, $"{idList.Count} items updated in the database");
                        }
                    }
                    else if (offer.OfferState == TradeOfferState.TradeOfferStateActive)
                    {
                        /*Check if the age of the offer is older than what we allow*/
                        /*If it's too old we want to remove it, but only if it's a deposit trade*/
                        if ((Functions.GetUnixTimestamp() - offer.TimeCreated) > mSettings.tradeOfferExpireTime && trade.tradeType == Config.TradeType.Deposit)
                        {
                            deleteList.Add(trade);
                            mLog.Write(Log.LogLevel.Info, $"Trade offer is too old");
                            trade.tradeStatus.Status = Config.TradeStatusType.DepositDeclined;

                            bool cancelResult = mBotHost.CancelTradeOffer(trade.tradeStatus.Tradelink);
                            mLog.Write(Log.LogLevel.Info, $"Q: Were we able to cancel the offer? A: {cancelResult}");
                        }
                    }
                    else if (offer.OfferState != TradeOfferState.TradeOfferStateNeedsConfirmation)
                    {
                        /*This offer has a a state that we don't want to deal with, so we'll remove it*/
                        deleteList.Add(trade);
                        trade.tradeStatus.Status = (trade.tradeType == Config.TradeType.Deposit) ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;
                        
                        /*If the offer has been countered then we'll decline it, else just leave it*/
                        if (offer.OfferState == TradeOfferState.TradeOfferStateCountered)
                        {
                            bool cancelResult = mBotHost.DeclineTradeOffer(trade.tradeStatus.Tradelink);
                            mLog.Write(Log.LogLevel.Info, $"Q: Were we able to decline the offer? A: {cancelResult}");
                        }
                    }
                }

                /*Remove all changed offers from the active trade list*/
                if (deleteList.Count > 0)
                    mActiveTradesList = mActiveTradesList.Except(deleteList).ToList();

                Website.UpdateTrade(deleteList);
                Thread.Sleep(7000);
            }
        }


        /// <summary>
        /// Returns a list of updated items from the steam inventory
        /// </summary>
        /// <param name="inventoryList">Steam inventory</param>
        /// <param name="requestItems">Items we want to update</param>
        /// <returns>Returns a list of items</returns>
        private List<Config.Item> FindUpdatedItems(List<SimpleInventory.InventoryItem> inventoryList, List<Config.Item> requestItems)
        {
            /*List that holds all items that we already used
            This is to prevent the same item being used twice*/
            var busyItems = new List<long>();

            /*Go through all the items that was requested*/
            foreach (var item in requestItems)
            {
                /*Go through all the items that the bot owns*/
                foreach (var inv in inventoryList)
                {
                    long assetid = inv.assetId;
                    if (item.ClassId == inv.classId && !mDatabase.IsUsedItem(assetid) && !busyItems.Contains(assetid))
                    {
                        /*Class id matched, there is no record of the item already being used in the database
                        and the local list of already used items does not contain the item
                        We can now use this item*/
                        item.AssetId = assetid;
                        busyItems.Add(assetid);
                        break;
                    }
                }
            }
            
            return requestItems;
        }


        /// <summary>
        /// Main trade background worker completed event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TradeWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
                mLog.Write(Log.LogLevel.Error, $"Unhandled exception in TradeWorkerBW thread: {e.Error}");

            mLog.Write(Log.LogLevel.Warn, $"mTradeWorker has exited.");
        }


        /// <summary>
        /// Queueworker died wtf are u want kid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MQueueWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
                mLog.Write(Log.LogLevel.Error, $"Unhandled exception in TradeWorkerBW thread: {e.Error}");

            mLog.Write(Log.LogLevel.Warn, $"mQueueWorker has exited.");
        }


        /// <summary>
        /// Daily scheduled restart that will re-boot the bots once a day at a given time
        /// </summary>
        /// <param name="sender">object</param>
        /// <param name="e">ElapsedEventArgs</param>
        private void ScheduledDailyRestart(object sender, ElapsedEventArgs e)
        {
            /*Set new elapsed timer interval 24 hours from now and alert for the restart*/
            mScheduledRestartTimer.Stop();
            mScheduledRestartTimer.Interval = new TimeSpan(24, 00, 00).TotalMilliseconds;
            mLog.Write(Log.LogLevel.Info, "Starting scheduled restart in 30 seconds.");
            mDiscord.PostMessage("Starting scheduled restart in 30 seconds.");

            /*Pause all services and wait 30 seconds for things to clear up*/
            mSessionState = SessionState.Locked;
            Thread.Sleep(30000);

            /*Restart all bots and verify cookies*/
            foreach (var bot in mBotList)
            {
                bot.Reconnect(true);
                bot.CheckCookies();
            }

            /*Enable service again*/
            mLog.Write(Log.LogLevel.Info, "Scheduled restart has been completed");
            mDiscord.PostMessage("Scheduled restart has been completed");
            mSessionState = SessionState.Active;
        }


        /// <summary>
        /// OnDiscordHelp Callback
        /// Posts all the commands available to the channel
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordHelp(CommandEventArgs e)
        {
            /*We'll convert all enum values to their text variant
            and join them together in a nice string.*/
            string comStr = string.Join(", !", 
                Enum.GetValues(typeof(Discord.CommandList))
                .Cast<Discord.CommandList>().ToList());

            /*PS. The first ! mark needs to be added manually
            don't remove this when "optimizing" later on*/
            e.Channel.SendMessage($"Commands available: !{comStr}");
        }


        /// <summary>
        /// OnDiscordCodes Callback
        /// Posts all the Steam Guard codes for all active bots
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordCodes(CommandEventArgs e)
        {
            string baseString = "Steam Guard Codes:";
            foreach (var bot in mBotList)
                baseString += $"\n{bot.mSettings.username}: {bot.GetSteamGuardCode()}";
            
            e.User.SendMessage($"```{baseString}```");
        }


        /// <summary>
        /// OnDiscordRestart Callback
        /// Pauses all actions and restarts all active bots
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordRestart(CommandEventArgs e)
        {
            /*We'll need to lock all actions before restarting the
            bots to avoid conflict. We'll also sleep to make sure
            we're not exiting mid-something*/

            e.Channel.SendMessage("Restarting all bots in 15 seconds.");
            mSessionState = SessionState.Locked;
            Thread.Sleep(15000);

            foreach (var bot in mBotList)
            {
                e.Channel.SendMessage($"Restarting {bot.mSettings.username}");
                bot.Reconnect(true);
            }

            e.Channel.SendMessage("Done");
            mSessionState = SessionState.Active;
        }


        /// <summary>
        /// OnDiscordStatus Callback
        /// Replies back status information for the bot
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordStatus(CommandEventArgs e)
        {
            /*List to hold all information*/
            var st = new List<string>();
            st.Add(string.Empty);

            /*Session information*/
            var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
            st.Add($"Session: {mSessionState}");
            st.Add($"Uptime: {uptime.Days} days, {uptime.Hours} hours, {uptime.Minutes} minutes, {uptime.Seconds} seconds");
            st.Add(string.Empty);

            /*Bot information*/
            st.Add($"Bots online: {mBotList.FindAll(o => o.mBotState == Bot.BotState.Connected).Count()}");
            st.Add($"Bots offline: {mBotList.FindAll(o => o.mBotState != Bot.BotState.Connected).Count()}");
            st.Add(string.Empty);

            /*Deposit trade information*/
            st.Add($"Deposit trade status");
            st.Add($"   Active trades: {mActiveTradesList.FindAll(o => o.tradeType == Config.TradeType.Deposit).Count()}");
            st.Add($"   In queue: {mActiveTradesListQueue.FindAll(o => o.tradeType == Config.TradeType.Deposit).Count()}");
            st.Add(string.Empty);

            /*Withdraw trade information*/
            st.Add($"Withdraw trade status");
            st.Add($"   Active trades: {mActiveTradesList.FindAll(o => o.tradeType == Config.TradeType.Withdraw).Count()}");
            st.Add($"   In queue: {mActiveTradesListQueue.FindAll(o => o.tradeType == Config.TradeType.Withdraw).Count()}");
            st.Add(string.Empty);

            /*Database information*/
            st.Add($"Items in database: {mDatabase.GetItemCount()}");

            e.Channel.SendMessage($"```{string.Join("\n", st)}```");
        }


        /// <summary>
        /// OnDiscordPause Callback
        /// Sets the bot in Paused state
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordPause(CommandEventArgs e)
        {
            mSessionState = SessionState.Paused;
            e.Channel.SendMessage($"Session has been set to: {mSessionState}");
        }


        /// <summary>
        /// OnDiscordPauseAll Callback
        /// Sets the bot in Locked state
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordPauseAll(CommandEventArgs e)
        {
            mSessionState = SessionState.Locked;
            e.Channel.SendMessage($"Session has been set to: {mSessionState}");
        }


        /// <summary>
        /// OnDiscordPause Callback
        /// Sets the bot in Paused state
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordUnpause(CommandEventArgs e)
        {
            mSessionState = SessionState.Active;
            e.Channel.SendMessage($"Session has been set to: {mSessionState}");
        }


        /// <summary>
        /// Clears all list of trade offers
        /// Be very careful when calling this
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordClear(CommandEventArgs e)
        {
            mActiveTradesList.Clear();
            mActiveTradesListQueue.Clear();
            e.Channel.SendMessage("All trades have been cleared");
        }
    }
}
