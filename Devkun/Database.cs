using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading;

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
            /// Bot storage account that holds the item
            /// Stored in BIGINT and represents SteamId64
            /// </summary>
            BotOwner,


            /// <summary>
            /// The user that owns the item
            /// Stored in BIGINT and represents SteamId64
            /// </summary>
            UserOwner,


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
        /// Database class constructor
        /// </summary>
        /// <param name="db">Local database</param>
        public Database(string db)
        {
            try
            {
                SQLiteConnection.CreateFile(db);

                mSqlCon = new SQLiteConnection($"Data Source={db};Version=3;");
                mSqlCon.Open();

                //string tableSql = "CREATE TABLE IF NOT EXIST items (BotOwner BIGINT, UserOwner BIGINT, AssetId BIGINT, ClassId BIGINT)";
                string tableSql = "CREATE TABLE items (BotOwner BIGINT, UserOwner BIGINT, AssetId BIGINT, ClassId BIGINT)";
                Insert(tableSql);

                if (mSqlCon.State == System.Data.ConnectionState.Open)
                    mConnected = true;
            }
            catch (System.IO.IOException)
            {
                Console.WriteLine("Could not connect to database. It's probably in use somewhere else.\n"
                    + "Morten, close SQLiteBrowser. Alternatively, copy the database somewhere else.");
            }
        }


        /// <summary>
        /// Inserts into database
        /// </summary>
        /// <param name="sql">sql query</param>
        public void Insert(string sql)
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
            string sql = "INSERT INTO items (BotOwner, UserOwner, AssetId, ClassId) values "
                    + $"({entry.botOwnerSteamId64}, {entry.userOwnerSteamId64}, {entry.AssetId}, {entry.ClassId})";

            SQLiteCommand command = new SQLiteCommand(sql, mSqlCon);
            command.ExecuteNonQuery();
        }


        /// <summary>
        /// Inserts a list of items into database
        /// </summary>
        /// <param name="entries">List of item entries</param>
        public void InsertItem(List<Config.Item> entries)
        {
            foreach (var entry in entries)
            {
                InsertItem(entry);
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

            string sql = $"SELECT * FROM items WHERE {lookup} = {key};";
            SQLiteCommand command = new SQLiteCommand(sql, mSqlCon);
            SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                itemList.Add(new Config.Item()
                {
                    botOwnerSteamId64 = (string)reader["BotOwner"],
                    userOwnerSteamId64 = (string)reader["UserOwner"],
                    AssetId = (string)reader["AssetId"],
                    ClassId = (string)reader["ClassId"]
                });
            }

            return itemList;
        }
    }
}
