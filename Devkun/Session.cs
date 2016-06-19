using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using SteamTrade.TradeOffer;
using System.ComponentModel;

namespace Devkun
{
    class Session
    {
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

            //Discord.Connect(mSettings.discord);
            //while (!Discord.mIsConnected) { Thread.Sleep(250); }

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

                /*If either of the above dies we want to alert here, and not proceed further*/
                while (mTradeWorker.IsBusy && mQueueWorker.IsBusy)
                    Thread.Sleep(500);

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
                    if (mBotHost.UserHasEscrowWaitingPeriod(trade))
                    {
                        mLog.Write(Log.LogLevel.Error, $"User {trade.SteamId} has an escrow waiting period. Will not continue.");
                        trade.tradeStatus.Status = trade.tradeType == Config.TradeType.Deposit ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;
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
                mActiveTradesList.Add(trade);
            }

            return trade.tradeStatus;
        }


        /// <summary>
        /// Deal with withdraw offers
        /// </summary>
        /// <param name="trade">TradeObject</param>
        private Config.TradeStatus HandleWithdraw(Config.TradeObject trade)
        {
            var itemlistList = new List<List<Config.Item>>();
            foreach (var item in trade.Items)
            {
                itemlistList.Add(mItemDB.FindEntry(Database.DBCols.ClassId, item.ClassId.ToString()));
            }

            var list = new List<Config.Item>();
            itemlistList.ForEach(o => list.Add(o.FirstOrDefault()));
            
            mLog.Write(Log.LogLevel.Info, $"Attempting to send withdraw trade offer to {trade.SteamId}");
            string offerId = mBotHost.SendTradeOffer(trade, Config.TradeType.Withdraw, $"{EndPoints.Website.DOMAIN.ToUpper()} DEPOSIT | {trade.SecurityToken}");

            return null;
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
                /*Local copy of main list*/
                var tradeList = mActiveTradesList.ToList();

                /*List of items that will be deleted from main list when this function has run*/
                var deleteList = new List<Config.TradeObject>();

                /*Go through the active trades list*/
                foreach (var trade in tradeList)
                {
                    /*Try to get the trade offer state*/
                    mLog.Write(Log.LogLevel.Info, $"Checking active {trade.tradeType} trade offer to user {trade.SteamId}");
                    
                    /*Get updated trade offer from api*/
                    TradeOffer offer = mBotHost.GetTradeOffer(trade.tradeStatus.Tradelink);
                    if (offer == null)
                    {
                        mLog.Write(Log.LogLevel.Warn, $"Trade offer returned null");
                        continue;
                    }

                    /*Get the state of the offer*/
                    mLog.Write(Log.LogLevel.Info, $"Trade offer status: {offer.OfferState}");
                    trade.offerState = offer.OfferState;

                    if (offer.OfferState == TradeOfferState.TradeOfferStateAccepted)
                    {
                        /*Trade offer was accepted, so add the items to the database*/
                        deleteList.Add(trade);
                        mItemDB.InsertItems(trade.Items);
                        mLog.Write(Log.LogLevel.Info, $"Items added to database");
                        trade.tradeStatus.Status = (trade.tradeType == Config.TradeType.Deposit) ? Config.TradeStatusType.DepositAccepted : Config.TradeStatusType.WithdrawAccepted;
                    }
                    else if (offer.OfferState == TradeOfferState.TradeOfferStateActive)
                    {
                        /*Check if the age of the offer is older than what we allow*/
                        /*If it's too old we want to remove it*/
                        if ((Functions.GetUnixTimestamp() - offer.TimeCreated) > mSettings.tradeOfferExpireTime)
                        {
                            deleteList.Add(trade);
                            mLog.Write(Log.LogLevel.Info, $"Trade offer is too old");
                            trade.tradeStatus.Status = (trade.tradeType == Config.TradeType.Deposit) ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;
                        }
                    }
                    else
                    {
                        /*The offer is whatever, don't care*/
                        deleteList.Add(trade);
                        trade.tradeStatus.Status = (trade.tradeType == Config.TradeType.Deposit) ? Config.TradeStatusType.DepositDeclined : Config.TradeStatusType.WithdrawDeclined;
                    }
                }

                /*Cancel the offers*/
                foreach (var trade in deleteList)
                {
                    /*Obviously only want to cancel if it hasn't been accepted*/
                    if (trade.offerState != TradeOfferState.TradeOfferStateAccepted)
                    {
                        bool cancelResult = false;

                        if (trade.offerState == TradeOfferState.TradeOfferStateCountered)
                            /*If the trade offer is countered we need to decline it rather than cancel*/
                            cancelResult = mBotHost.DeclineTradeOffer(trade.tradeStatus.Tradelink);
                        else
                            /*For the rest we can just cancel*/
                            cancelResult = mBotHost.CancelTradeOffer(trade.tradeStatus.Tradelink);

                        mLog.Write(Log.LogLevel.Info, $"Offer cancel result: {cancelResult}");
                    }
                }

                /*Remove all changed offers from the active trade list*/
                mActiveTradesList = mActiveTradesList.Except(deleteList).ToList();

                Website.UpdateTrade(deleteList);
                Thread.Sleep(7000);
            }
        }


        /// <summary>
        /// Main trade background worker completed event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TradeWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                mLog.Write(Log.LogLevel.Error, $"Unhandled exception in TradeWorkerBW thread: {e.Error}");
                //mTradeWorker.RunWorkerAsync();
            }

            mLog.Write(Log.LogLevel.Warn, $"TradeWorkerOnRunWorkerCompleted fired");
        }


        /// <summary>
        /// Queueworker died wtf are u want kid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MQueueWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                mLog.Write(Log.LogLevel.Error, $"Unhandled exception in TradeWorkerBW thread: {e.Error}");
                //mQueueWorker.RunWorkerAsync();
            }

            mLog.Write(Log.LogLevel.Warn, $"MQueueWorkerOnRunWorkerCompleted fired");
        }
    }
}
