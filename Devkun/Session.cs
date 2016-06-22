using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SteamTrade.TradeOffer;
using System.ComponentModel;
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
        /// Trade status
        /// </summary>
        private Config.TradeStatusHolder mTradeStatus { get; set; } = new Config.TradeStatusHolder();


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
        private Database mItemDB { get; set; }


        /// <summary>
        /// Discord client connection
        /// </summary>
        private Discord mDiscord { get; set; }


        /// <summary>
        /// Host bot used for trading with users
        /// </summary>
        private Bot mBotHost { get; set; }
        

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
            mDiscord = new Discord(settings.discord);

            mLog = new Log("Session", "Logs\\Session.txt", 3);
            mItemDB = new Database("Database\\Items.sqlite");
            if (!mItemDB.mConnected)
                return;

            if (AddBots(settings.bots))
            {
                /*Find first host bot in our list and assign it as host*/
                mBotHost = mBotList.FirstOrDefault(o => o.mBotType == Bot.BotType.Host);
                if (mBotHost == null)
                {
                    mLog.Write(Log.LogLevel.Error, "Hostbot is null");
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

                /*Here we'll read the input as hotkeys*/
                /*It will go be on a loop until either of the threads die*/
                ReadInput();

                /*Log information about which worker that isn't running*/
                mLog.Write(Log.LogLevel.Info,
                    $"A worker has exited."
                    + $"mTradeWorker running: {mTradeWorker.IsBusy}"
                    + $"mQueueWorker running: {mQueueWorker.IsBusy}");

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
        /// Reads input from console
        /// We'll use these instead of text commands
        /// </summary>
        private void ReadInput()
        {
            /*Only want to read keys while both our main threads are running*/
            /*If either dies we want to break the program*/
            while (mTradeWorker.IsBusy && mQueueWorker.IsBusy)
            {
                var input = Console.ReadKey();
                switch (input.Key)
                {
                    /*F1 - Post all codes*/
                    case ConsoleKey.F1:
                        {
                            mBotList.ForEach(o => Console.WriteLine(o.GetSteamGuardCode()));
                        }
                        break;
                }

                Thread.Sleep(1000);
            }
        }


        /// <summary>
        /// Initializes all the bots from settings
        /// </summary>
        /// <param name="botList">List of bots</param>
        private bool AddBots(List<AppSettings.BotSettings> botList)
        {
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

            /*Go through all added bots and start them*/
            foreach (var bot in mBotList)
            {
                bot.Connect();

                while (bot.mBotState != Bot.BotState.Connected)
                {
                    if (bot.mBotState == Bot.BotState.Error)
                    {
                        /*Just like when adding bots, if any bot fails to connect we want to return false and exit*/
                        mLog.Write(Log.LogLevel.Error, $"Bot {bot.mSettings.displayName} experienced an error when starting");
                        return false;
                    }

                    Thread.Sleep(500);
                }
            }

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
                mLog.Write(Log.LogLevel.Error, $"Format exception in GetQueue(). Message: {ex.Message}.");
                mLog.Write(Log.LogLevel.Error, $"Website response: {tradeJson}", true, false);
            }
            catch (JsonSerializationException ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Deserialization error when checking website response: {ex.Message}");
                mLog.Write(Log.LogLevel.Error, $"Website response: {tradeJson}", true, false);
            }
            catch (Exception ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Exception in GetQueue(). Message: {ex.Message}");
                mLog.Write(Log.LogLevel.Error, $"Website response: {tradeJson}", true, false);
            }

            Website.UpdateTrade(tradeList);
            return tradeList;
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
                /*Get trade queue from website*/
                var tradeList = GetQueue();
                foreach (var trade in tradeList)
                {
                    mLog.Write(Log.LogLevel.Debug, $"Examining {trade.tradeType} from {trade.SteamId}");

                    int daysTheirEscrow = mBotHost.GetUserEscrowWaitingPeriod(trade);
                    if (daysTheirEscrow > 0)
                    {
                        /*User has an escrow waiting period, which means we can't sent the trade to them*/
                        mLog.Write(Log.LogLevel.Error, $"User {trade.SteamId} has an escrow waiting period of {daysTheirEscrow} days. Will not continue.");
                        trade.tradeStatus.Status = trade.tradeType == Config.TradeType.Deposit ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;

                        /*123 is the default number that means something went wrong with the request*/
                        if (daysTheirEscrow == 123)
                            mLog.Write(Log.LogLevel.Info, $"123 days means that it failed to check how many days the user has left.");

                        continue;
                    }

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

                /*Authenticate trades*/
                mBotHost.ConfirmTrades();
                Website.UpdateTrade(tradeList);
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
                itemList.AddRange(mItemDB.FindEntry(Database.DBCols.ClassId, item.ClassId.ToString()));

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
                /*Offer was sent successfully*/
                mLog.Write(Log.LogLevel.Success, $"Withdraw trade offer was sent to user {trade.SteamId}");
                trade.tradeStatus.Status = Config.TradeStatusType.WithdrawSent;
                trade.tradeStatus.Tradelink = offerId;

                /*Add items sent to used items*/
                var assetList = trade.Items.Select(o => o.AssetId).ToList();
                mItemDB.InsertUsedItems(assetList);

                /*Set state to sent which means that next withdraw won't pick the same items*/
                var idList = trade.Items.Select(o => o.ID).ToList();
                mItemDB.UpdateItems(idList, Database.ItemState.Sent);
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
                            mItemDB.InsertItems(trade.Items);
                            mLog.Write(Log.LogLevel.Info, $"Items added to database");
                            trade.tradeStatus.Status = Config.TradeStatusType.DepositAccepted;
                        }
                        else
                        {
                            /*Set state to accepted which is final stage*/
                            /*These items will be moved to back-up database*/
                            var idList = trade.Items.Select(o => o.ID).ToList();
                            mItemDB.UpdateItems(idList, Database.ItemState.Accepted);
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
            var busyItems = new List<long>();
            foreach (var item in requestItems)
            {
                foreach (var inv in inventoryList)
                {
                    long assetid = inv.assetId;
                    if (item.ClassId == inv.classId && !mItemDB.IsUsedItem(assetid) && !busyItems.Contains(assetid))
                    {
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
    }
}
