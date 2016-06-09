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
using System.ComponentModel;

namespace Devkun
{
    class Bot
    {
        /// <summary>
        /// Bot settingns
        /// </summary>
        public AppSettings.BotSettings mSettings;


        /// <summary>
        /// What job this bot is assigned
        /// </summary>
        public BotType mBotType;


        /// <summary>
        /// What state the bot is in
        /// </summary>
        public BotState mBotState;


        /// <summary>
        /// If the bot is in running state
        /// In case of a disconnect it will reconnect if this is true
        /// </summary>
        public bool mIsRunning;


        /// <summary>
        /// Main bot thread
        /// </summary>
        private BackgroundWorker mBotThread;


        /// <summary>
        /// Steam class
        /// </summary>
        private Steam mSteam = new Steam();


        /// <summary>
        /// Bot log
        /// </summary>
        private Log mLog, mLogOffer;


        /// <summary>
        /// Bot type enum
        /// </summary>
        public enum BotType
        {
            Host,
            Storage
        }


        /// <summary>
        /// Bot state enum
        /// </summary>
        public enum BotState
        {
            None,
            Error,
            Disconnected,
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
                Password = settings.password
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
            mSteam.Inventory = new GenericInventory(mSteam.Web);

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
                    mLog.Write(Log.LogLevel.Error, $"{ex.Response}");
                    Thread.Sleep(10000);
                }
                catch (Exception ex)
                {
                    mLog.Write(Log.LogLevel.Error, $"Callback error: {ex}");
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
                Discord.SendMessage($"{mSettings.displayName} died unexpected! Restarting it...");
                mBotState = BotState.Disconnected;
            }

            if (mIsRunning)
            {
                Reconnect();
                mBotThread.RunWorkerAsync();
            }
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
        public void Connect()
        {
            mIsRunning = true;
            SteamDirectory.Initialize().Wait();
            mSteam.Client.Connect();
        }


        /// <summary>
        /// Reconnects the bot to Steam
        /// We do this to receive a fresh session
        /// </summary>
        public void Reconnect()
        {
            mSteam.Client.Disconnect();
            mLog.Write(Log.LogLevel.Error, "Disconnected from session.");
        }


        /// <summary>
        /// Disconnects the bot from Steam
        /// </summary>
        public void Disconnect()
        {
            mIsRunning = false;
            mBotState = BotState.Disconnected;

            mSteam.Client.Disconnect();
            mLog.Write(Log.LogLevel.Info, "Disconnected from session.");
        }


        /// <summary>
        /// Kills the bot entierly
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
        /// Request items from a deposit
        /// </summary>
        /// <param name="steamid">User steamid64</param>
        /// <param name="tradeToken">User trade token</param>
        /// <param name="message">Message to include in offer</param>
        /// <param name="itemList">List of item ids</param>
        /// <returns>Returns empty string if failed, else offerid</returns>
        public string SendTradeOffer(string steamid, string tradeToken, string message, List<Config.Item> itemList, Config.TradeType tradeType)
        {
            ulong steamidu;
            if (ulong.TryParse(steamid, out steamidu))
            {
                var offer = mSteam.TradeOfferManager.NewOffer(steamidu);
                mLogOffer.Write(Log.LogLevel.Debug, $"Created new trade offer to user {steamid} to deposit.");

                foreach (var item in itemList)
                {
                    long itemid;
                    if (long.TryParse(item.AssetId, out itemid))
                    {
                        switch (tradeType)
                        {
                            case Config.TradeType.Deposit:
                                offer.Items.AddTheirItem(730, 2, itemid);
                                mLogOffer.Write(Log.LogLevel.Debug, $"Added their item to trade. Item ID: {item}");
                                break;
                            case Config.TradeType.Withdraw:
                                offer.Items.AddMyItem(730, 2, itemid);
                                mLogOffer.Write(Log.LogLevel.Debug, $"Added my item to trade. Item ID: {item}");
                                break;
                        }
                    }
                    else
                    {
                        mLogOffer.Write(Log.LogLevel.Error, $"Unable to parse itemid {item} to user {steamid}");
                    }
                }
                
                return RequestTradeOffer(offer, tradeToken, message);
            }
            
            return string.Empty;
        }


        /// <summary>
        /// Sends trade offer to user
        /// </summary>
        /// <param name="offer">Offer to send</param>
        /// <returns>Returns empty if failed, else offer id</returns>
        private string RequestTradeOffer(TradeOffer offer, string tradeToken, string message)
        {
            string offerId = string.Empty;

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    if (offer.SendWithToken(out offerId, tradeToken, message))
                    {
                        mLogOffer.Write(Log.LogLevel.Debug, $"Trade offer sent to user {offer.PartnerSteamId} with id {offerId}");
                        break;
                    }

                    mLogOffer.Write(Log.LogLevel.Warn, $"Unable to send trade offer to user {offer.PartnerSteamId}. Trying again in 3 seconds.");
                    Thread.Sleep(3000);
                }
            }
            catch (WebException ex)
            {
                var webResponse = new StreamReader(ex.Response.GetResponseStream()).ReadToEnd();
                mLogOffer.Write(Log.LogLevel.Error, $"Web error sending offer to {offer.PartnerSteamId} - \nError: {ex.Message}\nResponse: {webResponse}");
            }
            catch (Exception ex)
            {
                mLogOffer.Write(Log.LogLevel.Error, $"Exception occured when sending offer to {offer.PartnerSteamId} - \nError: {ex.Message}");
            }

            return offerId;
        }


        /// <summary>
        /// On new trade offer event
        /// </summary>
        /// <param name="offer">Tradeoffer passed from event</param>
        private void TradeOfferManager_OnNewTradeOffer(TradeOffer offer)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Checks the state of a trade offer
        /// The account that sent it needs to be the account checking it
        /// </summary>
        /// <param name="tradeId">Trade id to check</param>
        /// <returns>Returns state of offer</returns>
        public TradeOfferState GetTradeOfferState(string offerId)
        {
            TradeOffer offer;
            mSteam.TradeOfferManager.GetOffer(offerId, out offer);

            if (offer != null)
                return offer.OfferState;

            return TradeOfferState.TradeOfferStateUnknown;
        }


        /// <summary>
        /// Cancels a trade offer by id
        /// </summary>
        /// <param name="offerId">Trade offer id</param>
        /// <returns>Returns true if cancelled</returns>
        public bool CancelTradeOffer(string offerId)
        {
            TradeOffer offer;
            mSteam.TradeOfferManager.GetOffer(offerId, out offer);

            if (offer != null)
                return offer.Cancel();

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
        public string GetBotSteamId64()
        {
            return mSteam.Client.SteamID.ConvertToUInt64().ToString();
        }


        /// <summary>
        /// Loads the inventory of this bot
        /// </summary>
        /// <returns>Returns inventory</returns>
        public Dictionary<ulong, GenericInventory.Item> GetInventory()
        {
            mSteam.Inventory.loadImplementation(730, new List<long>() { 2 }, mSteam.Client.SteamID);
            return mSteam.Inventory.items;
        }


        /// <summary>
        /// Loads the inventory of a user
        /// </summary>
        /// <param name="steamId">Steam id of user</param>
        /// <returns>Returnns items</returns>
        public Dictionary<ulong, GenericInventory.Item> GetInventory(ulong steamId)
        {
            mSteam.Inventory.loadImplementation(730, new List<long>() { 2 }, steamId);
            return mSteam.Inventory.items;
        }
    }
}
