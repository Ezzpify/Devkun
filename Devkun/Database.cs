using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;
using System.Data.Linq;
using System.IO;

namespace Devkun
{
    class Database
    {
        /// <summary>
        /// Represents the columns in our item database
        /// </summary>
        public enum DBCols
        {
            /// <summary>
            /// Item entry id
            /// This is auto increasing with each entry
            /// </summary>
            ID,


            /// <summary>
            /// Bot storage account that holds the item
            /// Stored in BIGINT and represents SteamId64
            /// </summary>
            BotOwner,


            /// <summary>
            /// The temporary item inventory id
            /// Stored in BIGINT and represents item asset id
            /// </summary>
            AssetId,


            /// <summary>
            /// Item id bound to each item type
            /// Stored in BIGINT and represents item class id
            /// </summary>
            ClassId
        }


        /// <summary>
        /// Item database state
        /// </summary>
        public enum ItemState
        {
            /// <summary>
            /// We have the item in our inventory, and it's free to withdraw
            /// </summary>
            Active,


            /// <summary>
            /// We have sent the item to a user, but not yet accepted
            /// </summary>
            Sent,


            /// <summary>
            /// Item has been accepted by user, this can be moved to history
            /// </summary>
            Accepted,


            /// <summary>
            /// Item that has been put on hold that will be sent to storage
            /// </summary>
            OnHold
        }


        /// <summary>
        /// Our database connection
        /// </summary>
        private SQLiteConnection mSqlCon;


        /// <summary>
        /// Represents if database is connected
        /// </summary>
        public bool mConnected;


        /// <summary>
        /// Database log
        /// </summary>
        private Log mLog;


        /// <summary>
        /// Database class constructor
        /// </summary>
        /// <param name="db">Local database</param>
        public Database(string db)
        {
            mLog = new Log("Database", "Database\\Log.txt", 3);

            try
            {
                if (!File.Exists(db))
                {
                    SQLiteConnection.CreateFile(db);
                    mLog.Write(Log.LogLevel.Info, $"Created item database at {db}");
                }

                mSqlCon = new SQLiteConnection($"Data Source={db};Version=3;");
                mSqlCon.Open();

                if (mSqlCon.State == System.Data.ConnectionState.Open)
                {
                    Insert("CREATE TABLE IF NOT EXISTS items (ID INTEGER PRIMARY KEY AUTOINCREMENT, BotOwner BIGINT, AssetId BIGINT, ClassId BIGINT, ItemState INTEGER DEFAULT 0)");
                    Insert("CREATE TABLE IF NOT EXISTS useditems (AssetId BIGINT)");
                    mConnected = true;
                }
            }
            catch (IOException ex)
            {
                mLog.Write(Log.LogLevel.Error, $"Could not connect to database. {ex.Message}");
            }
        }


        /// <summary>
        /// Inserts into database
        /// </summary>
        /// <param name="sql">sql query</param>
        private void Insert(string sql)
        {
            SQLiteCommand cmd = new SQLiteCommand(sql, mSqlCon);
            cmd.ExecuteNonQuery();
        }


        /// <summary>
        /// Inserts an item that has been sent and accepted, this can't be sent twice
        /// </summary>
        /// <param name="ids">List of item asset ids</param>
        public void InsertUsedItems(List<long> ids)
        {
            using (var cmd = new SQLiteCommand("INSERT INTO useditems (AssetId) values (?)", mSqlCon))
            {
                using (var transaction = mSqlCon.BeginTransaction())
                {
                    foreach (var id in ids)
                    {
                        cmd.Parameters.AddWithValue("AssetId", id);
                        cmd.ExecuteNonQuery();
                    }

                    try
                    {
                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        mLog.Write(Log.LogLevel.Info, $"Error commiting multiple items ex: {ex.Message}");
                    }
                }
            }
        }


        /// <summary>
        /// Checks if an item exists in our useditems table
        /// </summary>
        /// <param name="assetid">assetid of items</param>
        /// <returns>Returns true if item exist</returns>
        public bool IsUsedItem(long assetid)
        {
            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM useditems WHERE AssetId = '{assetid}'", mSqlCon))
            {
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }


        /// <summary>
        /// Returns the amount of items in database
        /// </summary>
        /// <returns>Returns long</returns>
        public long GetItemCount()
        {
            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM items", mSqlCon))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }


        /// <summary>
        /// Inserts item entry into database
        /// </summary>
        /// <param name="entry">Item entry</param>
        public void InsertItem(Config.Item entry)
        {
            using (var cmd = new SQLiteCommand("INSERT INTO items (BotOwner, AssetId, ClassId) values (?, ?, ?)", mSqlCon))
            {
                cmd.Parameters.AddWithValue("BotOwner", entry.BotOwner);
                cmd.Parameters.AddWithValue("AssetId", entry.AssetId);
                cmd.Parameters.AddWithValue("ClassId", entry.ClassId);

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    mLog.Write(Log.LogLevel.Error, $"Error inserting item {entry.ClassId} ex: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Inserts a list of item entries in a transation
        /// </summary>
        /// <param name="entries">List of item entries</param>
        public void InsertItems(List<Config.Item> entries)
        {
            using (var cmd = new SQLiteCommand("INSERT INTO items (BotOwner, AssetId, ClassId) values (?, ?, ?)", mSqlCon))
            {
                using (var transaction = mSqlCon.BeginTransaction())
                {
                    foreach (var entry in entries)
                    {
                        cmd.Parameters.AddWithValue("BotOwner", entry.BotOwner);
                        cmd.Parameters.AddWithValue("AssetId", entry.AssetId);
                        cmd.Parameters.AddWithValue("ClassId", entry.ClassId);

                        cmd.ExecuteNonQuery();
                    }

                    try
                    {
                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        mLog.Write(Log.LogLevel.Info, $"Error commiting multiple items ex: {ex.Message}");
                    }
                }
            }
        }


        /// <summary>
        /// Updates items in database
        /// </summary>
        /// <param name="ids">List of ids that we should update</param>
        /// <param name="state">What state we should update them to</param>
        public void UpdateItemStates(List<int> ids, ItemState state)
        {
            using (var transaction = mSqlCon.BeginTransaction())
            {
                foreach (var id in ids)
                {
                    using (var cmd = new SQLiteCommand($"UPDATE items SET ItemState = {(int)state} WHERE ID = {id}", mSqlCon))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                try
                {
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    mLog.Write(Log.LogLevel.Info, $"Error updating items ex: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Updates the bot owner of items
        /// </summary>
        /// <param name="trade">tradeobject</param>
        /// <param name="ownerId">owner to update to</param>
        public void UpdateItemOwners(Config.TradeObject trade)
        {
            using (var transaction = mSqlCon.BeginTransaction())
            {
                foreach (var item in trade.Items)
                {
                    using (var cmd = new SQLiteCommand($"UPDATE items SET BotOwner = {trade.SteamId} WHERE ID = {item.ID}", mSqlCon))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                try
                {
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    mLog.Write(Log.LogLevel.Info, $"Error updating items ex: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Updates the assetid of items
        /// </summary>
        /// <param name="trade">tradeobject</param>
        /// <param name="ownerId">owner to update to</param>
        public void UpdateItemAssetIds(Config.TradeObject trade)
        {
            using (var transaction = mSqlCon.BeginTransaction())
            {
                foreach (var item in trade.Items)
                {
                    using (var cmd = new SQLiteCommand($"UPDATE items SET AssetId = {item.AssetId} WHERE ID = {item.ID}", mSqlCon))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                try
                {
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    mLog.Write(Log.LogLevel.Info, $"Error updating items ex: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Static find entry function that returns a list of item entries depending on key given
        /// </summary>
        /// <param name="lookup">What column we should match with</param>
        /// <param name="key">Key to find in column</param>
        /// <returns>Returns list of ItemEntry</returns>
        public List<Config.Item> FindEntries(DBCols lookup, string key)
        {
            var itemList = new List<Config.Item>();
            
            using (var cmd = new SQLiteCommand($"SELECT * FROM items WHERE {lookup} = {key} AND ItemState = 0", mSqlCon))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        itemList.Add(new Config.Item()
                        {
                            ID = Convert.ToInt32(reader["ID"]),
                            BotOwner = Convert.ToUInt64(reader["BotOwner"]),
                            AssetId = Convert.ToInt64(reader["AssetId"]),
                            ClassId = Convert.ToInt64(reader["ClassId"]),
                            State = Convert.ToInt32(reader["ItemState"])
                        });
                    }
                }
            }

            mLog.Write(Log.LogLevel.Debug, $"Query returned {itemList.Count} results");
            return itemList;
        }
    }
}
