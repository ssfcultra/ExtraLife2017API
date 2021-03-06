﻿using ExtraLife2017Functions.Constants;
using ExtraLife2017Functions.Interfaces;
using ExtraLife2017Functions.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ExtraLife2017Functions.Repositories
{
    /// <summary>
    /// Stores the data in a json file so that no database is required for this
    /// sample application
    /// </summary>
    public class PrizeRepository : IPrizeRepository
    {
        public string MongoDbConnectionString { get; set; }
        public IMongoDatabase Database { get; private set; }

        public PrizeRepository()
        {
            var variables = Environment.GetEnvironmentVariables();
            MongoDbConnectionString = ConfigurationManager
                .ConnectionStrings[StringIdentifiers.MongoDbConnectionStringName]
                ?.ConnectionString
                ??
                Environment.GetEnvironmentVariable(StringIdentifiers.MongoDbConnectionStringName);
            Database = InitializeMongoDb();
        }

        /// <summary>
        /// Creates a new product with default values
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Prize> Create()
        {
            var Prize = new List<Prize>()
            {
                new Prize()
                {
                    DateAdded = DateTime.Now
                }
            };
            return Prize;
        }

        /// <summary>
        /// Retrieves the list of prizes.
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<Prize>> RetrieveAsync()
        {
            await InsertTheInitialData();
            var collection = Database.GetCollection<BsonDocument>(StringIdentifiers.ExtraLife2017Prizes);
            var prizes = await collection.FindAsync<Prize>(new BsonDocument()).Result.ToListAsync();

            return prizes;
        }

        /// <summary>
        /// Saves a new prizes.
        /// </summary>
        /// <param name="prizes"></param>
        /// <returns></returns>
        public async Task<IEnumerable<Prize>> SaveAsync(Prize prize)
        {
            // Read in the existing prizes
            var result = await RetrieveAsync();
            var prizes = result.ToList();

            prize.PrizeId = prizes.Max(p => p.PrizeId) + 1; // Assign a new Id
            var utcNow = new DateTimeOffset(DateTime.UtcNow);
            prize.DateAdded = ((new DateTimeOffset(prize.DateAdded) - new DateTimeOffset(DateTime.UtcNow)).Minutes >= 5)
                ? prize.DateAdded.ToUniversalTime()
                : DateTime.UtcNow; // if the timestamp is not in the future by more than five minutes, use DateTime.UtcNow

            var prizesToAdd = new List<Prize>() { prize };
            EnsureAllFieldsHaveEntries(prizesToAdd);            
            await WriteDataAsync(prizesToAdd);
            return prizesToAdd;
        }

        /// <summary>
        /// Updates an existing product
        /// </summary>
        /// <param name="id"></param>
        /// <param name="prize"></param>
        /// <returns></returns>
        public async Task<IEnumerable<Prize>> SaveAsync(int id, Prize prize)
        {
            // Read in the existing products
            var result = await RetrieveAsync();
            var prizes = result.ToList();

            // Locate the item
            var itemIndex = prizes.FindIndex(p => p.PrizeId == prize.PrizeId);
            if (itemIndex < 1) // Ids start at 1
            {
                return null;
            }

            await WriteDataAsync(new List<Prize>() { prize }, isUpdate: true);
            return new List<Prize>() { prize };
        }

        public async Task<IEnumerable<Prize>> SaveAsync(List<Prize> prizes)
        {
            // Read in the existing prizes
            var result = await RetrieveAsync();
            var nextPrizeID = result.ToList().Max(p => p.PrizeId) + 1;

            foreach(var prize in prizes)
            {
                prize.PrizeId = prizes.Max(p => p.PrizeId) + 1; // Assign a new Id
                var utcNow = new DateTimeOffset(DateTime.UtcNow);
                prize.DateAdded = ((new DateTimeOffset(prize.DateAdded) - new DateTimeOffset(DateTime.UtcNow)).Minutes >= 5)
                    ? prize.DateAdded.ToUniversalTime()
                    : DateTime.UtcNow; // if the timestamp is not in the future by more than five minutes, use DateTime.UtcNow
                nextPrizeID++;
            }
            EnsureAllFieldsHaveEntries(prizes);
            await WriteDataAsync(prizes);
            return prizes;
        }

        public async Task<bool> WriteDataAsync(List<Prize> prizes, bool isUpdate = false)
        {
            // assign Guids for IDs
            prizes.Where(p => p._id == Guid.Empty)
                .ToList()
                .ForEach(p => p._id = Guid.NewGuid());

            List<BsonDocument> documents = prizes.ConvertAll(p => p.ToBsonDocument());

            var collection = Database.GetCollection<BsonDocument>(StringIdentifiers.ExtraLife2017Prizes);

            if (isUpdate && prizes.Count == 1)
            {
                var filter = Builders<BsonDocument>.Filter.Eq("PrizeId", prizes.FirstOrDefault().PrizeId);
                var update = documents.FirstOrDefault();
                await collection.ReplaceOneAsync(filter, update);
            }
            else if (prizes.Count == 1) // Use InsertOneAsync for single BsonDocument insertion.
            {
                await collection.InsertOneAsync(documents.FirstOrDefault());
            }
            else
            {
                await collection.InsertManyAsync(documents);
            }
            return true;
        }

        public IMongoDatabase InitializeMongoDb()
        {
            var settings = MongoClientSettings.FromUrl(new MongoUrl(MongoDbConnectionString));
            settings.SslSettings = new SslSettings { EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 };
            var client = new MongoClient(settings);
            return client.GetDatabase(StringIdentifiers.DbName);
        }

        public async Task InsertTheInitialData()
        {
            // http://mongodb.github.io/mongo-csharp-driver/2.4/getting_started/quick_tour/
            var collection = Database.GetCollection<BsonDocument>(StringIdentifiers.ExtraLife2017Prizes);
            var count = await collection.CountAsync(new BsonDocument());

            if (count == 0)
            {
                var prizeData = CreateSeedData();
                EnsureAllFieldsHaveEntries(prizeData);

                // Use InsertOneAsync for single BsonDocument insertion.
                await WriteDataAsync(prizeData);
            }
        }

        private static void EnsureAllFieldsHaveEntries(List<Prize> prizeData)
        {
            for (int i = 0; i < prizeData.Count; i++)
            {
                var prize = prizeData[i];

                if (prize._id == Guid.Empty)
                {
                    prize._id = Guid.NewGuid();
                }

                if (prize.PrizeId == 0)
                {
                    prize.PrizeId = i + 1;
                }

                if (prize.DateToDisplay == DateTime.MinValue)
                {
                    prize.DateToDisplay = DateTime.UtcNow;
                }

                if (prize.DateAdded == DateTime.MinValue)
                {
                    prize.DateAdded = DateTime.UtcNow;
                }
            }
        }

        private List<Prize> CreateSeedData()
        {
            var path = string.Empty;

            // ToDo: there's got to be a better way for this
            if (string.Compare(Environment.MachineName, StringIdentifiers.KeithsMachineName, ignoreCase: true) == 0)
            {
                path = StringIdentifiers.SampleDataLocalPath;
            }
            else { path = StringIdentifiers.SampleDataLocalPathAzure; }

            var jsonInput = File.ReadAllText(path);
            return BsonSerializer.Deserialize<List<Prize>>(jsonInput);
        }
    }
}
