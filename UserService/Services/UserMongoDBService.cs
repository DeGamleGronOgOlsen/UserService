using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UserService.Models;

namespace Services
{
    public class UserMongoDBService : IUserDBRepository
    {
        private readonly IMongoCollection<User> _userCollection;
        private readonly ILogger<UserMongoDBService> _logger;

        public UserMongoDBService(ILogger<UserMongoDBService> logger, IConfiguration configuration)
        {
            _logger = logger;
            var connectionString = configuration["ConnectionString"];
            var databaseName = configuration["DatabaseName"] ?? "admin";
            var collectionName = configuration["CollectionName"] ?? "Users";

            _logger.LogInformation($"Connected to MongoDB using: {connectionString}");
            _logger.LogInformation($"Using database: {databaseName}");
            _logger.LogInformation($"Using Collection: {collectionName}");

            try
            {
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase(databaseName);
                _userCollection = database.GetCollection<User>(collectionName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect to MongoDB: {ex}");
                throw;
            }
        }

        public async Task<User> CreateUserAsync(User user)
        {
            await _userCollection.InsertOneAsync(user);
            return user;
        }

        public async Task<User> GetUserByIdAsync(Guid id)
        {
            return await _userCollection.Find(u => u.Id == id).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            return await _userCollection.Find(_ => true).ToListAsync();
        }

        public async Task<User> UpdateUserAsync(Guid id, User updatedUser)
        {
            var result = await _userCollection.ReplaceOneAsync(u => u.Id == id, updatedUser);
            return result.ModifiedCount > 0 ? updatedUser : null;
        }

        public async Task<bool> DeleteUserAsync(Guid id)
        {
            var result = await _userCollection.DeleteOneAsync(u => u.Id == id);
            return result.DeletedCount > 0;
        }
    }
}
