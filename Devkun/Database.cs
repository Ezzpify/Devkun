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
                    Insert("CREATE TABLE IF NOT EXISTS items (ID INTEGER PRIMARY KEY AUTOINCREMENT, BotOwner BIGINT, AssetId BIGINT, ClassId BIGINT, Active BOOLEAN DEFAULT TRUE)");
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
            SQLiteCommand command = new SQLiteCommand(sql, mSqlCon);
            command.ExecuteNonQuery();
        }


        /// <summary>
        /// Inserts item entry into database
        /// </summary>
        /// <param name="entry">Item entry</param>
        public void InsertItem(Config.Item entry)
        {
            using (var insert = new SQLiteCommand("INSERT INTO items (BotOwner, AssetId, ClassId) values (?, ?, ?)", mSqlCon))
            {
                insert.Parameters.AddWithValue("BotOwner", entry.BotOwner);
                insert.Parameters.AddWithValue("AssetId", entry.AssetId);
                insert.Parameters.AddWithValue("ClassId", entry.ClassId);

                try
                {
                    insert.ExecuteNonQuery();
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
            using (var insert = new SQLiteCommand("INSERT INTO items (BotOwner, AssetId, ClassId) values (?, ?, ?)", mSqlCon))
            {
                using (var transaction = mSqlCon.BeginTransaction())
                {
                    foreach (var entry in entries)
                    {
                        insert.Parameters.AddWithValue("BotOwner", entry.BotOwner);
                        insert.Parameters.AddWithValue("AssetId", entry.AssetId);
                        insert.Parameters.AddWithValue("ClassId", entry.ClassId);

                        insert.ExecuteNonQuery();
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
        /// Static find entry function that returns a list of item entries depending on key given
        /// </summary>
        /// <param name="lookup">What column we should match with</param>
        /// <param name="key">Key to find in column</param>
        /// <returns>Returns list of ItemEntry</returns>
        public List<Config.Item> FindEntry(DBCols lookup, string key)
        {
            var itemList = new List<Config.Item>();

            SQLiteCommand command = new SQLiteCommand($"SELECT * FROM items WHERE {lookup} = {key};", mSqlCon);
            SQLiteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                itemList.Add(new Config.Item()
                {
                    ID = Convert.ToInt32(reader["ID"]),
                    BotOwner = Convert.ToUInt64(reader["BotOwner"]),
                    AssetId = Convert.ToInt64(reader["AssetId"]),
                    ClassId = Convert.ToInt64(reader["ClassId"]),
                    Active = Convert.ToBoolean(reader["Active"])
                });
            }

            mLog.Write(Log.LogLevel.Debug, $"Query returned {itemList.Count} results");
            return itemList;
        }
    }
}
