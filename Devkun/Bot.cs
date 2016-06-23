using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SteamKit2;
using SteamKit2.Internal;
using SteamTrade;
using SteamTrade.TradeOffer;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Devkun
{
    class Bot
    {
        /// <summary>
        /// Bot settingns
        /// </summary>
        public AppSettings.BotSettings mSettings { get; set; }


        /// <summary>
        /// What state the bot is in
        /// </summary>
        public BotState mBotState { get; set; }


        /// <summary>
        /// What job this bot is assigned
        /// </summary>
        public BotType mBotType { get; set; }


        /// <summary>
        /// If the bot is in running state
        /// In case of a disconnect it will reconnect if this is true
        /// </summary>
        public bool mIsRunning { get; set; }


        /// <summary>
        /// Steam class
        /// </summary>
        private Steam mSteam { get; set; } = new Steam();


        /// <summary>
        /// Main bot thread
        /// </summary>
        private BackgroundWorker mBotThread { get; set; }


        /// <summary>
        /// Bot log
        /// </summary>
        private Log mLog, mLogOffer;


        /// <summary>
        /// Bot type enum
        /// </summary>
        public enum BotType
        {
            /// <summary>
            /// Host bot will talk to user, taking deposits and sending out withdraws
            /// </summary>
            Main = 1,


            /// <summary>
            /// Storage bot will only store items
            /// </summary>
            Storage = 2
        }


        /// <summary>
        /// Bot state enum
        /// </summary>
        public enum BotState
        {
            /// <summary>
            /// We don't know what the state with this bot is
            /// </summary>
            Unknown,


            /// <summary>
            /// An error has occured somewhere and this bot is likely not working
            /// </summary>
            Error,


            /// <summary>
            /// Bot is currently disconnected from service
            /// </summary>
            Disconnected,


            /// <summary>
            /// Bot in connected an in working state
            /// </summary>
            Connected
        }


        /// <summary>
        /// Class consntructor
        /// </summary>
        /// <param name="settings">Settings for this account</param>
        public Bot(AppSettings.BotSettings settings)
        {
            mSettings = settings;
            mBotType = (BotType)settings.jobId;
            mLog = new Log(settings.displayName, $"Logs\\Bot\\{settings.displayName}.txt", 3);
            mLogOffer = new Log(settings.displayName, $"Logs\\Bot\\{settings.displayName} Offers.txt", 3);

            mSteam.sentryPath = Functions.GetStartFolder() + $"Sentryfiles\\{settings.username}.sentry";
            mSteam.logOnDetails = new SteamUser.LogOnDetails()
            {
                Username = settings.username,
                Password = settings.password,
                ShouldRememberPassword = true
            };
            mSteam.Web = new SteamWeb();
            ServicePointManager.ServerCertificateValidationCallback += mSteam.Web.ValidateRemoteCertificate;

            mSteam.Client = new SteamClient();
            mSteam.Trade = mSteam.Client.GetHandler<SteamTrading>();
            mSteam.CallbackManager = new CallbackManager(mSteam.Client);
            mSteam.User = mSteam.Client.GetHandler<SteamUser>();
            mSteam.Friends = mSteam.Client.GetHandler<SteamFriends>();
            mSteam.Auth = new Authentication(settings);
            
            mSteam.CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            mSteam.CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            mSteam.CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            mSteam.CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
            mSteam.CallbackManager.Subscribe<SteamUser.WebAPIUserNonceCallback>(OnWebAPIUserNonce);
            mSteam.CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnUpdateMachineAuth);

            mBotThread = new BackgroundWorker { WorkerSupportsCancellation = true };
            mBotThread.DoWork += BackgroundWorkerOnDoWork;
            mBotThread.RunWorkerCompleted += BackgroundWorkerOnRunWorkerCompleted;
            mBotThread.RunWorkerAsync();

            mLog.Write(Log.LogLevel.Info, "Initialized");
        }


        /// <summary>
        /// OnConnected callback
        /// Fires when steam connects
        /// </summary>
        /// <param name="callback">ConnectedCallback</param>
        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                mLog.Write(Log.LogLevel.Error, $"EResult from connected callback was not OK! {callback.Result}");
                mBotState = BotState.Error;
                return;
            }

            byte[] sentryHash = null;
            if (File.Exists(mSteam.sentryPath))
            {
                byte[] sentryFile = File.ReadAllBytes(mSteam.sentryPath);
                sentryHash = CryptoHelper.SHAHash(sentryFile);
            }

            mLog.Write(Log.LogLevel.Info, "Connected! Logging in...", false);
            mSteam.logOnDetails.SentryFileHash = sentryHash;
            mSteam.User.LogOn(mSteam.logOnDetails);
        }


        /// <summary>
        /// OnDisconnected callback
        /// Fires when steam disconnected
        /// </summary>
        /// <param name="callback">DisconnectedCallback</param>
        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            mBotState = BotState.Disconnected;
            if (mIsRunning)
            {
                mLog.Write(Log.LogLevel.Info, "Reconnecting in three seconds...", false);
                Thread.Sleep(3000);
                Connect();
            }
        }


        /// <summary>
        /// OnLoggedOn callback
        /// Fires when steam logs on successfully
        /// </summary>
        /// <param name="callback">LoggedOnCallback</param>
        private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result == EResult.AccountLoginDeniedNeedTwoFactor || callback.Result == EResult.TwoFactorCodeMismatch)
            {
                mLog.Write(Log.LogLevel.Info, "Fetching Steam Guard code");
                mSteam.logOnDetails.TwoFactorCode = mSteam.Auth.GetSteamGuardCode();
                return;
            }

            if (callback.Result != EResult.OK)
            {
                if (callback.Result == EResult.ServiceUnavailable)
                {
                    mLog.Write(Log.LogLevel.Error, $"Steam is down, waiting for 20 seconds - {callback.Result}");
                    Thread.Sleep(20000);
                    return;
                }

                mLog.Write(Log.LogLevel.Error, $"Unable to login to Steam - {callback.Result}");
                Thread.Sleep(2500);
                return;
            }

            mLog.Write(Log.LogLevel.Info, "Logged in! Authenticating...", false);
            mSteam.nounce = callback.WebAPIUserNonce;
        }


        /// <summary>
        /// OnLoginKey callback
        /// Fires when we receive the login key
        /// </summary>
        /// <param name="callback">LoginKeyCallback</param>
        private void OnLoginKey(SteamUser.LoginKeyCallback callback)
        {
            mSteam.uniqueId = callback.UniqueID.ToString();
            mSteam.logOnDetails.LoginKey = callback.LoginKey;
            UserWebAuthenticate();
        }


        /// <summary>
        /// OnWebAPIUserNonce callback
        /// Fires when we receive a new nonce
        /// </summary>
        /// <param name="callback">WebAPIUserNonceCallback</param>
        private void OnWebAPIUserNonce(SteamUser.WebAPIUserNonceCallback callback)
        {
            mLog.Write(Log.LogLevel.Info, "Received new WebAPIUserNonce");
            if (callback.Result == EResult.OK)
            {
                mSteam.nounce = callback.Nonce;
                UserWebAuthenticate();
            }
            else
            {
                mLog.Write(Log.LogLevel.Error, $"WebAPIUserNonce error: {callback.Result}");
            }
        }


        /// <summary>
        /// Authenticate with web api
        /// Last point before we are fully connected
        /// </summary>
        private void UserWebAuthenticate()
        {
            while (mBotState != BotState.Connected)
            {
                if (mSteam.Web.Authenticate(mSteam.uniqueId, mSteam.Client, mSteam.nounce))
                {
                    mBotState = BotState.Connected;
                    break;
                }

                mLog.Write(Log.LogLevel.Warn, "Web authentication failed, retrying in three seconds...");
                Thread.Sleep(3000);
            }

            mLog.Write(Log.LogLevel.Success, $"User authenticated! Type: {mBotType}", false);
            mSteam.TradeOfferManager = new TradeOfferManager(mSettings.apiKey, mSteam.Web);
            mSteam.TradeOfferManager.OnNewTradeOffer += TradeOfferManager_OnNewTradeOffer;
            mSteam.Inventory = new SimpleInventory(mSteam.Web);

            mSteam.Friends.SetPersonaName(mSettings.displayName);
            mSteam.Friends.SetPersonaState(EPersonaState.Online);

            var gamesPlaying = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
            gamesPlaying.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed() { game_id = 730 });
            mSteam.Client.Send(gamesPlaying);
        }


        /// <summary>
        /// OnUpdateMachineAuth callback
        /// Fires when we need to update the auth file
        /// </summary>
        /// <param name="callback">UpdateMachineAuthCallback</param>
        private void OnUpdateMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            int fileSize;
            byte[] sentryHash;
            using (var fs = File.Open(mSteam.sentryPath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(callback.Offset, SeekOrigin.Begin);
                fs.Write(callback.Data, 0, callback.BytesToWrite);
                fileSize = (int)fs.Length;

                fs.Seek(0, SeekOrigin.Begin);
                using (var sha = new SHA1CryptoServiceProvider())
                {
                    sentryHash = sha.ComputeHash(fs);
                }
            }

            mSteam.User.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,
                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = fileSize,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,
                SentryFileHash = sentryHash,
            });
        }


        /// <summary>
        /// Main background worker for bot
        /// </summary>
        private void BackgroundWorkerOnDoWork(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            while (!mBotThread.CancellationPending)
            {
                try
                {
                    mSteam.CallbackManager.RunCallbacks();
                    Thread.Sleep(500);
                }
                catch (WebException ex)
                {
                    string exResp = new StreamReader(ex.Response.GetResponseStream()).ReadToEnd();
                    mLog.Write(Log.LogLevel.Error, $"Callback webexception: {exResp}");
                    Thread.Sleep(10000);
                }
                catch (Exception ex)
                {
                    mLog.Write(Log.LogLevel.Error, $"Callback exception: {ex}");
                    Thread.Sleep(10000);
                }
            }
        }


        /// <summary>
        /// Main background worker complete
        /// </summary>
        private void BackgroundWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs runWorkerCompletedEventArgs)
        {
            if (runWorkerCompletedEventArgs.Error != null)
            {
                Exception ex = runWorkerCompletedEventArgs.Error;
                mLog.Write(Log.LogLevel.Error, $"Unhandled exceptions in callback thread: {ex}");
                mBotState = BotState.Disconnected;
            }
        }


        /// <summary>
        /// Request items from a deposit
        /// </summary>
        /// <param name="steamid">User steamid64</param>
        /// <param name="tradeToken">User trade token</param>
        /// <param name="message">Message to include in offer</param>
        /// <param name="itemList">List of item ids</param>
        /// <returns>Returns empty string if failed, else offerid</returns>
        public string SendTradeOffer(Config.TradeObject trade, Config.TradeType tradeType, string message)
        {
            var offer = mSteam.TradeOfferManager.NewOffer(trade.SteamId);
            mLogOffer.Write(Log.LogLevel.Debug, $"Created new trade offer to user {trade.SteamId} to {tradeType} with security token {trade.SecurityToken}.");

            foreach (var item in trade.Items)
            {
                switch (tradeType)
                {
                    /*For deposit we want to add their items*/
                    case Config.TradeType.Deposit:
                        offer.Items.AddTheirItem(730, 2, item.AssetId);
                        mLogOffer.Write(Log.LogLevel.Debug, $"Added their item to trade. Item ID: {item.ClassId}");
                        break;

                    /*As for withdraw we want to add our items*/
                    case Config.TradeType.Withdraw:
                        offer.Items.AddMyItem(730, 2, item.AssetId);
                        mLogOffer.Write(Log.LogLevel.Debug, $"Added my item to trade. Item ID: {item.ClassId}");
                        break;
                }
            }
                
            return RequestTradeOffer(offer, trade, message);
        }


        /// <summary>
        /// Sends trade offer to user
        /// </summary>
        /// <param name="offer">Offer to send</param>
        /// <returns>Returns empty if failed, else offer id</returns>
        private string RequestTradeOffer(TradeOffer offer, Config.TradeObject trade, string message)
        {
            string offerId = string.Empty;
            string exceptionMsg = string.Empty;

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (offer.SendWithToken(out offerId, trade.RU_Token, message))
                    {
                        mLogOffer.Write(Log.LogLevel.Debug, $"Trade offer sent to user {offer.PartnerSteamId} with id {offerId}");
                        break;
                    }
                }
                catch (WebException ex)
                {
                    string resp = new StreamReader(ex.Response.GetResponseStream()).ReadToEnd();
                    exceptionMsg = $"Webexeption: {resp}";
                }
                catch (Exception ex)
                {
                    exceptionMsg = $"Exception: {ex.Message}";
                }

                mLogOffer.Write(Log.LogLevel.Warn, $"Unable to send trade offer to user {trade.SteamId}. Trying again in 3 seconds.");
                Thread.Sleep(3000);
            }

            if (string.IsNullOrWhiteSpace(offerId))
                mLogOffer.Write(Log.LogLevel.Error, $"Failed to send trade offer to user {trade.SteamId}. Error: {exceptionMsg}");

            return offerId;
        }


        /// <summary>
        /// Reads how many days of escrow the user has
        /// </summary>
        /// <param name="trade">Trade object containing user information</param>
        /// <returns>Returns EscrowDuration class, if failed returns null</returns>
        public int GetUserEscrowWaitingPeriod(Config.TradeObject trade)
        {
            string url = "https://steamcommunity.com/tradeoffer/new/";

            SteamID steamId = new SteamID();
            steamId.SetFromUInt64(trade.SteamId);

            var data = new NameValueCollection();
            data.Add("partner", steamId.AccountID.ToString());
            data.Add("token", trade.RU_Token);

            string response = string.Empty;

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    response = mSteam.Web.Fetch(url, "GET", data, false);
                }
                catch (WebException ex)
                {
                    var webResponse = new StreamReader(ex.Response.GetResponseStream()).ReadToEnd();
                    mLogOffer.Write(Log.LogLevel.Error, $"Web exception when getting user escrow waiting period at {ex.Message}");
                }
            }

            return Functions.ParseEscrowResponse(response);
        }


        /// <summary>
        /// On new trade offer event
        /// </summary>
        /// <param name="offer">Tradeoffer passed from event</param>
        private void TradeOfferManager_OnNewTradeOffer(TradeOffer offer)
        {
            /*We should send a very detailed message with the offer and update database after we get it?*/
            /*Idk*/
            throw new NotImplementedException();
        }


        /// <summary>
        /// Check cookie state
        /// </summary>
        /// <returns>Returns true if cookies are valid</returns>
        public bool CheckCookies()
        {
            try
            {
                if (!mSteam.Web.VerifyCookies())
                {
                    mLog.Write(Log.LogLevel.Warn, "Cookies are invalid, need to refresh");
                    mSteam.User.RequestWebAPIUserNonce();
                    return false;
                }
            }
            catch
            {
                mLog.Write(Log.LogLevel.Warn, "Cookie check failed. Steamcommunity might be down.");
            }

            return true;
        }


        /// <summary>
        /// Connect the bot to Steam
        /// </summary>
        /// <param name="wait">If thread should wait for bot to be connected again</param>
        public void Connect(bool wait = false)
        {
            mIsRunning = true;
            SteamDirectory.Initialize().Wait();
            mSteam.Client.Connect();

            if (wait)
            {
                mLog.Write(Log.LogLevel.Info, $"Waiting for {mSettings.displayName} to connect before resuming thread");
                while (mBotState != BotState.Connected)
                {
                    if (mBotState == BotState.Error)
                    {
                        mLog.Write(Log.LogLevel.Error, $"Bot {mSettings.displayName} encountered an error when connecting.");
                        break;
                    }

                    Thread.Sleep(250);
                }
            }
        }


        /// <summary>
        /// Reconnects the bot to Steam
        /// We do this to receive a fresh session
        /// </summary>
        /// <param name="wait">If thread should wait for bot to be connected again</param>
        public void Reconnect(bool wait = true)
        {
            Disconnect(true);

            if (wait)
            {
                mLog.Write(Log.LogLevel.Info, $"Waiting for {mSettings.displayName} to connect before resuming thread");
                while (mBotState != BotState.Connected)
                {
                    if (mBotState == BotState.Error)
                    {
                        mLog.Write(Log.LogLevel.Error, $"Bot {mSettings.displayName} encountered an error when connecting.");
                        break;
                    }

                    Thread.Sleep(250);
                }
            }
        }


        /// <summary>
        /// Disconnects the bot from Steam
        /// </summary>
        /// <param name="retry">If true steam will reconnect</param>
        public void Disconnect(bool retry = false)
        {
            if (!retry)
                mIsRunning = false;

            mBotState = BotState.Disconnected;
            mSteam.Client.Disconnect();
            mLog.Write(Log.LogLevel.Info, "Disconnected from session.");
        }


        /// <summary>
        /// Kills the bot entierly
        /// There is no fucking way of coming back from this.
        /// We'll execute it, right inbetween the eyes.
        /// </summary>
        public void Kill()
        {
            mIsRunning = false;
            mBotState = BotState.Disconnected;

            mSteam.Client.Disconnect();
            mBotThread.CancelAsync();
            mLog.Write(Log.LogLevel.Info, "Killed bot.");
        }


        /// <summary>
        /// Checks the state of a trade offer
        /// The account that sent it needs to be the account checking it
        /// </summary>
        /// <param name="tradeId">Trade id to check</param>
        /// <returns>Returns state of offer</returns>
        public TradeOffer GetTradeOffer(string offerId)
        {
            TradeOffer offer = null;

            try
            {
                mSteam.TradeOfferManager.GetOffer(offerId, out offer);
            }
            catch (Exception ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Error getting trade offer: {ex.Message}");
            }

            return offer;
        }


        /// <summary>
        /// Cancels a trade offer by id
        /// </summary>
        /// <param name="offerId">Trade offer id</param>
        /// <returns>Returns true if cancelled</returns>
        public bool CancelTradeOffer(string offerId)
        {
            TradeOffer offer = GetTradeOffer(offerId);
            if (offer != null)
                return offer.Cancel();

            return false;
        }


        /// <summary>
        /// Decline a trade offer by id
        /// </summary>
        /// <param name="offerId">Trade offer to decline</param>
        /// <returns>Returns true if declined</returns>
        public bool DeclineTradeOffer(string offerId)
        {
            TradeOffer offer = GetTradeOffer(offerId);
            if (offer != null)
                return offer.Decline();

            return false;
        }


        /// <summary>
        /// Returns steam guard access code
        /// </summary>
        /// <returns>String</returns>
        public string GetAuthCode()
        {
            return mSteam.Auth.GetSteamGuardCode();
        }


        /// <summary>
        /// Returns bot steam id 64
        /// </summary>
        /// <returns>Returns string</returns>
        public ulong GetBotSteamId64()
        {
            return mSteam.Client.SteamID.ConvertToUInt64();
        }


        /// <summary>
        /// Returns bot inventory
        /// </summary>
        /// <param name="reload">If we should reload the inventory or just get the old one</param>
        /// <returns>Returns simple inventory items list</returns>
        public List<SimpleInventory.InventoryItem> GetInventory()
        {
            mSteam.Inventory.Load(GetBotSteamId64(), 730, 2);
            mLog.Write(Log.LogLevel.Debug, $"Loaded {mSteam.Inventory.Items.Count} items");
            return mSteam.Inventory.Items;
        }


        /// <summary>
        /// Returns inventory of a steam user
        /// </summary>
        /// <param name="steamid">SteamId64 of user</param>
        /// <returns>Returns simple inventory items list</returns>
        public List<SimpleInventory.InventoryItem> GetInventory(ulong steamid)
        {
            mSteam.Inventory.Load(steamid, 730, 2);
            mLog.Write(Log.LogLevel.Debug, $"Loaded {mSteam.Inventory.Items.Count} items from {steamid}'s inventory");
            return mSteam.Inventory.Items;
        }


        /// <summary>
        /// Returns the steam guard login code for the account
        /// </summary>
        /// <returns>Returns string</returns>
        public string GetSteamGuardCode()
        {
            return mSteam.Auth.GetSteamGuardCode();
        }


        /// <summary>
        /// Returns all pending confirmations
        /// </summary>
        /// <returns>List of Confirmation</returns>
        public void ConfirmTrades()
        {
            foreach (var confirmation in mSteam.Auth.GetConfirmationList())
            {
                if (confirmation != null)
                {
                    try
                    {
                        if (mSteam.Auth.mAccount.AcceptConfirmation(confirmation))
                            mLog.Write(Log.LogLevel.Info, $"Trade offer {confirmation.ConfirmationKey} confirmed");
                    }
                    catch (Exception ex)
                    {
                        mLog.Write(Log.LogLevel.Warn, $"Exception occured when trying to confirm trade: {ex.Message}");
                    }
                }
            }
        }
    }
}
