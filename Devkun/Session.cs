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
        /// List of active storage trades
        /// </summary>
        private List<Config.TradeObject> mActiveStorageTradesList { get; set; } = new List<Config.TradeObject>();


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
        /// List of pending withdraw trades that failed
        /// </summary>
        private List<Config.TradeObject> mPendingWithdraw { get; set; } = new List<Config.TradeObject>();


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
        /// Represents the state of the two threads
        /// </summary>
        private bool mQueueSleeping, mWorkSleeping;


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

                /*Connect to Discord*/
                mDiscord = new Discord(this, settings.discord);

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
                //mScheduledRestartTimer = new System.Timers.Timer();
                //mScheduledRestartTimer.Interval = (new TimeSpan(08, 00, 00) - DateTime.Now.TimeOfDay).TotalMilliseconds;
                //mScheduledRestartTimer.Elapsed += new ElapsedEventHandler(ScheduledDailyRestart);
                //mScheduledRestartTimer.Start();

                /*We'll pause here until either thread breaks*/
                /*It will go be on a loop until either of the threads die*/
                Console.Clear();
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
                {
                    mWorkSleeping = true;
                    Thread.Sleep(500);
                }
                mWorkSleeping = false;

                /*We'll wait for the storage offers to complete before we continue*/
                while (mActiveStorageTradesList.Count > 0)
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

                /*Go through all pending withdraw offers that failed*/
                foreach (var trade in mPendingWithdraw)
                    trade.tradeStatus = HandleWithdraw(trade);
                
                /*We'll only send items to storage if the queue is empty*/
                if (mActiveTradesList.Count == 0 && mActiveTradesListQueue.Count == 0)
                    SendItemsToStorage();

                /*Authenticate trades*/
                Website.UpdateTrade(tradeList);
                mBotHost.ConfirmTrades();
                Thread.Sleep(5000);
            }
        }


        /// <summary>
        /// Sends items to storage bots
        /// </summary>
        private void SendItemsToStorage()
        {
            /*Check how many items in the database that belongs to the host bot*/
            var hostItemList = mDatabase.FindEntries(Database.DBCols.BotOwner, mBotHost.GetBotSteamId64().ToString());
            mLog.Write(Log.LogLevel.Debug, $"Host owns {hostItemList.Count} items that can be sent to storage");
            if (hostItemList.Count >= mSettings.hostItemLimit)
            {
                mLog.Write(Log.LogLevel.Info, $"We have {hostItemList.Count} items stored on Host. Moving some to storage.");

                /*Go through all storage bots*/
                foreach (var bot in mBotList.Where(o => o.mBotType == Bot.BotType.Storage))
                {
                    /*Load the inventory and skip if we have too many items*/
                    var inventory = bot.GetInventory(true);
                    if (inventory.Count() >= mSettings.itemLimitPerBot)
                        continue;

                    /*Get amount of free slots on the bot
                    Although we need to limit it by n to ensure a stable offer*/
                    int inventorySlots = mSettings.itemLimitPerBot - inventory.Count();
                    if (inventorySlots > mSettings.storageTradeOfferMaxItems)
                        inventorySlots = mSettings.storageTradeOfferMaxItems;

                    /*Take items from the original list
                    We also need to make sure we have more than one item to trade*/
                    var itemList = hostItemList.Take(inventorySlots).ToList();
                    if (itemList.Count() > 0)
                    {
                        /*Set up a trade object to the storage bot*/
                        var trade = new Config.TradeObject()
                        {
                            Items = FindUpdatedItems(mBotHost.GetInventory(), itemList),
                            RU_Token = bot.mSettings.tradeToken,
                            SteamId = bot.GetBotSteamId64(),
                            tradeType = Config.TradeType.Withdraw,
                            tradeStatus = new Config.TradeStatus()
                        };

                        /*Send trade offer from host bot to this bot*/
                        var offerId = mBotHost.SendTradeOffer(trade, EndPoints.Steam.STORAGE_MESSAGE);
                        if (string.IsNullOrWhiteSpace(offerId))
                        {
                            mLog.Write(Log.LogLevel.Error, $"Storage offer failed to send? Beep boop error pls fix");
                        }
                        else
                        {
                            mLog.Write(Log.LogLevel.Success, $"Storage offer sent to bot {bot.mSettings.username}");
                            trade.tradeStatus.Tradelink = offerId;

                            /*Add items sent to used items in the database*/
                            mDatabase.InsertUsedItems(trade.Items.Select(o => o.AssetId).ToList());

                            /*Set item state to storage sent*/
                            var idList = trade.Items.Select(o => o.ID).ToList();
                            mDatabase.UpdateItemStates(idList, Database.ItemState.OnHold);
                            mLog.Write(Log.LogLevel.Info, $"{idList.Count} items updated in the database to OnHold");
                            mActiveStorageTradesList.Add(trade);
                        }
                    }

                    /*Since we got this far then we can stop checking*/
                    break;
                }

                Thread.Sleep(1500);
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
            string offerId = mBotHost.SendTradeOffer(trade, $"{EndPoints.Website.DOMAIN.ToUpper()} DEPOSIT | {trade.SecurityToken}");

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
                itemList.AddRange(mDatabase.FindEntries(Database.DBCols.ClassId, item.ClassId.ToString()));

            /*Sort them so we'll best the best possible bots*/
            itemList = Functions.SortDBItems(itemList, trade.Items);
            mLog.Write(Log.LogLevel.Info, $"Found {itemList.Count} perfect items. Requested count: {trade.Items.Count}");

            /*We won't really do anything about this, but it's good to keep track of*/
            if (itemList.Count < trade.Items.Count)
                mLog.Write(Log.LogLevel.Error, $"We found less items than what was requested");

            /*Group up all the items depending on the owner*/
            var itemGroups = itemList.GroupBy(o => o.BotOwner);

            /*Go through all the groups*/
            foreach (var group in itemGroups)
            {
                /*Find the bot that owns this item based on steamid*/
                var bot = mBotList.Find(o => o.GetBotSteamId64() == group.Key);

                /*Make sure we don't want to send a trade from hostbot to hostbot
                because obviously that won't work, dummy. And there's no need to*/
                if (bot.GetBotSteamId64() == mBotHost.GetBotSteamId64())
                    continue;

                /*Set up a trade object*/
                var storageTrade = new Config.TradeObject()
                {
                    Items = FindUpdatedItems(bot.GetInventory(), group.ToList()),
                    RU_Token = mBotHost.mSettings.tradeToken,
                    SteamId = mBotHost.GetBotSteamId64(),
                    tradeType = Config.TradeType.Withdraw,
                    tradeStatus = new Config.TradeStatus()
                };

                /*Send the trade offer*/
                var storeOfferId = bot.SendTradeOffer(storageTrade, EndPoints.Steam.STORAGE_MESSAGE);
                if (string.IsNullOrWhiteSpace(storeOfferId))
                {
                    mLog.Write(Log.LogLevel.Error, $"Inhouse offer wasn't sent");
                }
                else
                {
                    mLog.Write(Log.LogLevel.Success, $"Inhouse trade offer sent");
                    mDatabase.InsertUsedItems(storageTrade.Items.Select(o => o.AssetId).ToList());
                    storageTrade.tradeStatus.Id = storeOfferId;
                    Thread.Sleep(1000);
                    bot.ConfirmTrades();
                }

                Console.WriteLine($"Boop: {storeOfferId}");
            }

            /*Accept all trades on hostbot*/
            Thread.Sleep(5000);
            mBotHost.GetOffers();
            
            /*Get the new itemids for each item*/
            var newItemList = FindUpdatedItems(mBotHost.GetInventory(), itemList);
            if (newItemList.Count < itemList.Count)
            {
                mLog.Write(Log.LogLevel.Info, $"We're missing items to withdraw. Trying again next round.");
                return trade.tradeStatus;
            }
            else
            {
                /*Set the updated list to trade list*/
                trade.Items = newItemList;
            }

            /*Send the trade offer*/
            mLog.Write(Log.LogLevel.Info, $"Attempting to send withdraw trade offer to {trade.SteamId}");
            string offerId = mBotHost.SendTradeOffer(trade, $"{EndPoints.Website.DOMAIN.ToUpper()} DEPOSIT | {trade.SecurityToken}");

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

                /*Set state to sent which means that next withdraw won't pick the same items*/
                var idList = trade.Items.Select(o => o.ID).ToList();
                mDatabase.UpdateItemStates(idList, Database.ItemState.Sent);
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
                {
                    mQueueSleeping = true;
                    Thread.Sleep(500);
                }
                mQueueSleeping = false;

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
                    if (offer.OfferState == TradeOfferState.TradeOfferStateAccepted)
                    {
                        deleteList.Add(trade);
                        if (trade.tradeType == Config.TradeType.Deposit)
                        {
                            /*The trade was a deposit, so we'll enter the items to the database*/
                            mDatabase.InsertItems(trade.Items);
                            mLog.Write(Log.LogLevel.Info, $"Items added to database");
                            trade.tradeStatus.Status = Config.TradeStatusType.DepositAccepted;
                        }
                        else if (trade.tradeType == Config.TradeType.Withdraw)
                        {
                            /*Set state to accepted which is final stage*/
                            /*These items will be moved to back-up database*/
                            var idList = trade.Items.Select(o => o.ID).ToList();
                            mDatabase.UpdateItemStates(idList, Database.ItemState.Accepted);
                            mLog.Write(Log.LogLevel.Info, $"{idList.Count} items updated in the database");
                            trade.tradeStatus.Status = Config.TradeStatusType.WithdrawAccepted;
                        }
                    }
                    else if (offer.OfferState == TradeOfferState.TradeOfferStateActive)
                    {
                        /*Check if the age of the offer is older than what we allow*/
                        /*If it's too old we want to remove it, but only if it's a deposit trade*/
                        if ((Functions.GetUnixTimestamp() - offer.TimeCreated) > mSettings.tradeOfferExpireTimeSeconds 
                            && trade.tradeType == Config.TradeType.Deposit)
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
                        trade.tradeStatus.Status = (trade.tradeType == Config.TradeType.Deposit) 
                            ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;
                        
                        /*If the offer has been countered then we'll decline it, else just leave it*/
                        if (offer.OfferState == TradeOfferState.TradeOfferStateCountered)
                        {
                            bool cancelResult = mBotHost.DeclineTradeOffer(trade.tradeStatus.Tradelink);
                            mLog.Write(Log.LogLevel.Info, $"Q: Were we able to decline the offer? A: {cancelResult}");
                        }
                    }
                }

                /*Go through all storage offers*/
                foreach (var trade in mActiveStorageTradesList)
                {
                    /*Try to get the trade offer state*/
                    mLog.Write(Log.LogLevel.Info, $"Checking active storage trade offer");

                    /*Get updated trade offer from api*/
                    TradeOffer offer = mBotHost.GetTradeOffer(trade.tradeStatus.Tradelink);
                    if (offer == null)
                    {
                        mLog.Write(Log.LogLevel.Warn, $"Trade offer returned null");
                        trade.errorCount++;
                        continue;
                    }

                    /*Get offers for the bot that has the offer pending and accept them*/
                    var bot = mBotList.FirstOrDefault(o => o.GetBotSteamId64() == trade.SteamId);
                    bot?.GetOffers();

                    /*We only care if it's active*/
                    if (offer.OfferState == TradeOfferState.TradeOfferStateAccepted)
                    {
                        mLog.Write(Log.LogLevel.Info, $"Storage trade offer accepted. Updating items.");

                        /*Offer was accepted, so we'll first update the owners and then set the itemstate back to active (0)*/
                        mDatabase.UpdateItemOwners(trade);

                        /*Update the itemstate to be active again*/
                        var idList = trade.Items.Select(o => o.ID).ToList();
                        mDatabase.UpdateItemStates(idList, Database.ItemState.Active);
                        deleteList.Add(trade);
                    }
                }

                /*Remove all changed offers from the active trade list*/
                if (deleteList.Count > 0)
                {
                    mActiveTradesList = mActiveTradesList.Except(deleteList).ToList();
                    mActiveStorageTradesList = mActiveStorageTradesList.Except(deleteList).ToList();
                }

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
                    if (item.ClassId == inv.classId 
                        && !mDatabase.IsUsedItem(assetid) 
                        && !busyItems.Contains(assetid))
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
        /// Sets session state to locked and wait until both threads are sleeping
        /// </summary>
        private void LockThreadsAndWait()
        {
            mSessionState = SessionState.Locked;
            while (!mWorkSleeping || !mQueueSleeping)
            {
                mLog.Write(Log.LogLevel.Info, $"Waiting for threads to sleep");
                Thread.Sleep(5000);
            }
        }


        /// <summary>
        /// OnDiscordHelp Callback
        /// Posts all the commands available to the channel
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordHelp(CommandEventArgs e)
        {
            string baseString = "Available commands:\n";
            foreach (var command in mDiscord.CommandDictionary)
                baseString += $"\n{command.Key} - {command.Value}";

            e.Channel.SendMessage($"```{baseString}```");
        }


        /// <summary>
        /// OnDiscordCodes Callback
        /// Posts all the Steam Guard codes for all active bots
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordCodes(CommandEventArgs e)
        {
            string baseString = $"Active bots: {mBotList.Count}\n";
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
            LockThreadsAndWait();

            /*Restart each bot*/
            foreach (var bot in mBotList)
            {
                e.Channel.SendMessage($"Restarting {bot.mSettings.username}");
                bot.Reconnect(true);
                e.Channel.SendMessage($"Bot status: {bot.mBotState}");
            }

            e.Channel.SendMessage("Restart completed. Starting in 10 seconds.");
            Thread.Sleep(10000);
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
        /// Returns a comment list containing all the active offers
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordGetOffers(CommandEventArgs e)
        {
            string builder = $"Active offers: {mActiveTradesList.Count}\n";
            foreach (var offer in mActiveTradesList)
            {
                builder += $"\n{offer.SteamId}";
                builder += $"\n     QueueId: {offer.QueId}";
                builder += $"\n     Type: {offer.tradeType}";
                builder += $"\n     Errors: {offer.errorCount}";
                builder += $"\n     Status id: {offer.tradeStatus.Id}\n";
            }

            e.Channel.SendMessage($"```{builder}```");
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
            LockThreadsAndWait();

            /*Clear the offers*/
            mActiveTradesList.Clear();
            mActiveTradesListQueue.Clear();
            e.Channel.SendMessage("All trades have been cleared");
            mSessionState = SessionState.Active;
        }


        /// <summary>
        /// Removes active trade offer from list by queue id
        /// </summary>
        /// <param name="e">CommandEventArgs</param>
        public void OnDiscordRemoveOffer(CommandEventArgs e)
        {
            LockThreadsAndWait();

            /*Remove all matching offers*/
            int result = mActiveTradesList.RemoveAll(o => o.QueId == e.GetArg(0));
            e.Channel.SendMessage($"Removed {result} active trade offers");
            mSessionState = SessionState.Active;
        }
    }
}
