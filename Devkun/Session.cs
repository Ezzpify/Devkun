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
        private Log mLog, mLogDatabase;


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
            mLogDatabase = new Log("Database", "Logs\\Database.txt", 3);

            mItemDB = new Database("Items.sqlite");
            if (!mItemDB.mConnected)
                return;

            if (AddBots(settings.bots) && StartBots())
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
                mQueueWorker.RunWorkerCompleted += MQueueWorker_RunWorkerCompleted;
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

            return mBotList.Count > 0;
        }


        /// <summary>
        /// Connects all bots to steam
        /// </summary>
        /// <returns>Returns true if all bot started</returns>
        private bool StartBots()
        {
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

            return true;
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
                    mLog.Write(Log.LogLevel.Warn, "Website response in TradeWorkerOnDoWork was empty?");
                }
                else
                {
                    /*Attempt to deserialize the response we got here*/
                    var trade = JsonConvert.DeserializeObject<Config.Trade>(tradeJson);

                    /*Go through all deposit objects*/
                    foreach (var t in trade.Deposits)
                    {
                        t.tradeType = Config.TradeType.Deposit;
                        t.tradeStatus = new Config.TradeStatus() { Id = t.QueId, SteamId = t.SteamId, Status = "1" };

                        foreach (var item in t.item_Ids)
                        {
                            /*Since ids come in format assetId;classId we need to manually split them here*/
                            var iSplit = item.Split(';');
                            t.Items.Add(new Config.Item() { AssetId = iSplit[0], ClassId = iSplit[1], userOwnerSteamId64 = t.SteamId, botOwnerSteamId64 = mBotHost.GetBotSteamId64() });
                        }

                        tradeList.Add(t);
                    }

                    /*Go through all withdraw objects*/
                    foreach (var t in trade.withdrawal)
                    {
                        t.tradeType = Config.TradeType.Withdraw;
                        t.tradeStatus = new Config.TradeStatus() { Id = t.QueId, SteamId = t.SteamId, Status = "1" };

                        foreach (var item in t.item_Ids)
                        {
                            /*Same as for deposit*/
                            var iSplit = item.Split(';');
                            t.Items.Add(new Config.Item() { AssetId = iSplit[0], ClassId = iSplit[1], userOwnerSteamId64 = t.SteamId, botOwnerSteamId64 = mBotHost.GetBotSteamId64() });
                        }

                        tradeList.Add(t);
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
                            }
                            break;
                    }
                }

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
            string offerId = mBotHost.SendTradeOffer(trade.SteamId, trade.RU_Token,
                $"{EndPoints.Website.DOMAIN.ToUpper()} DEPOSIT | {trade.SecurityToken}",
                trade.Items, Config.TradeType.Deposit);

            if (string.IsNullOrWhiteSpace(offerId))
            {
                /*Trade offer id was empty, so that means the offer failed to send*/
                mLog.Write(Log.LogLevel.Error, $"Deposit trade offer was not sent to user {trade.SteamId}");
                trade.tradeStatus.Status = "2";
            }
            else
            {
                /*Offer was sent successfully*/
                mLog.Write(Log.LogLevel.Success, $"Deposit trade offer was sent to user {trade.SteamId}");
                trade.tradeStatus.Status = "3";
                trade.tradeStatus.Tradelink = offerId;
                mActiveTradesList.Add(trade);
            }

            return trade.tradeStatus;
        }


        /// <summary>
        /// Deal with withdraw offers
        /// </summary>
        /// <param name="trade">TradeObject</param>
        private void HandleWithdraw(Config.TradeObject trade)
        {
            
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
                var tradeList = new List<Config.TradeObject>();

                /*Go through the active trades list*/
                foreach (var trade in mActiveTradesList.ToList())
                {
                    /*Try to get the trade offer state*/
                    mLog.Write(Log.LogLevel.Info, $"Checking offer to user {trade.SteamId}");
                    trade.offerState = mBotHost.GetTradeOfferState(trade.tradeStatus.Tradelink);
                    mLog.Write(Log.LogLevel.Info, $"Trade offer status: {trade.offerState}");

                    if (trade.offerState == TradeOfferState.TradeOfferStateUnknown
                        || trade.offerState == TradeOfferState.TradeOfferStateActive)
                        continue;
                    
                    switch (trade.offerState)
                    {
                        /*The trade offer was accepted, so we'll set it to accepted here*/
                        case TradeOfferState.TradeOfferStateAccepted:
                            trade.tradeStatus.Status = "4";
                            break;

                        /*Anything but accepted, active or unknown. This means we want to decline it*/
                        default:
                            trade.tradeStatus.Status = "2";
                            break;
                    }

                    tradeList.Add(trade);
                }

                /*Go through all the trades that has been changed*/
                foreach (var trade in tradeList)
                {
                    if (trade.offerState == TradeOfferState.TradeOfferStateAccepted)
                    {
                        /*This trade was accepted, so enter the items to the database*/
                        mItemDB.InsertItem(trade.Items);
                        mLog.Write(Log.LogLevel.Info, $"Items put in database");
                    }
                    else
                    {
                        /*This means that something else happened to the offer, so we'll cancel it here*/
                        mLog.Write(Log.LogLevel.Info, $"Trade offer state is {trade.offerState} and we will cancel it.");
                        mLog.Write(Log.LogLevel.Info, $"Cancel result: {mBotHost.CancelTradeOffer(trade.tradeStatus.Tradelink)}");
                    }
                }
                
                Website.UpdateTrade(tradeList);
                Thread.Sleep(5000);

                /*Remove all changed offers from the active trade list*/
                mActiveTradesList = mActiveTradesList.Except(tradeList).ToList();
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
        }


        /// <summary>
        /// Queueworker died wtf are u want kid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MQueueWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                mLog.Write(Log.LogLevel.Error, $"Unhandled exception in TradeWorkerBW thread: {e.Error}");
                //mQueueWorker.RunWorkerAsync();
            }
        }
    }
}
