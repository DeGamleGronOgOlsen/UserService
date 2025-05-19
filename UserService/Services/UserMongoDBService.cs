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
            // Log the creation of the user
            _logger.LogInformation($"User created with ID: {user.Id}");
            _logger.LogInformation($"User created with Username: {user.Username}");
            _logger.LogInformation($"User created with Role: {user.Role}");
            // Error handling
            if (user == null)
            {
                _logger.LogError("Failed to create user");
                throw new Exception("Failed to create user");
            }
            return user;
        }

        public async Task<User> GetUserByIdAsync(Guid id)
        {                    
            // Error handling
            if (id == Guid.Empty)
            {
                _logger.LogError("Invalid user ID");
                throw new ArgumentException("Invalid user ID");
            }
            // Log the retrieval of the user
            _logger.LogInformation($"User retrieved with ID: {id}");
            return await _userCollection.Find(u => u.Id == id).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {            
            // Error handling
            if (_userCollection == null)
            {
                _logger.LogError("User collection is null");
                throw new Exception("User collection is null");
            }
            // Log the retrieval of all users
            _logger.LogInformation("Retrieving all users");
            return await _userCollection.Find(_ => true).ToListAsync();
        }

        public async Task<User> UpdateUserAsync(Guid id, User updatedUser)
        {
            var result = await _userCollection.ReplaceOneAsync(u => u.Id == id, updatedUser);
            // Error handling
            if (result.MatchedCount == 0)
            {
                _logger.LogError($"User with ID: {id} not found");
                throw new Exception($"User with ID: {id} not found");
            }
            // Log the retrieval of all users
            _logger.LogInformation("Retrieving all users");
            return result.ModifiedCount > 0 ? updatedUser : null;
        }

        public async Task<bool> DeleteUserAsync(Guid id)
        {
            var result = await _userCollection.DeleteOneAsync(u => u.Id == id);
            // Error handling
            if (result.DeletedCount == 0)
            {
                _logger.LogError($"User with ID: {id} not found");
                throw new Exception($"User with ID: {id} not found");
            }
            // Log the deletion of the user
            _logger.LogInformation($"User deleted with ID: {id}");
            return result.DeletedCount > 0;
        }
    }
}
