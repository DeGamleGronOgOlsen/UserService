using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration; // Required for IConfiguration
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UserService.Models; // Assuming your User model is in this namespace

namespace Services // Assuming this is the namespace for your service classes
{
    public class UserMongoDBService : IUserDBRepository
    {
        private readonly IMongoCollection<User> _userCollection;
        private readonly ILogger<UserMongoDBService> _logger;

        public UserMongoDBService(ILogger<UserMongoDBService> logger, IConfiguration configuration)
        {
            _logger = logger;

            // Read MongoDB configuration from IConfiguration using hierarchical keys
            // These keys are expected to be set in Program.cs after fetching from Vault.
            // Example in Program.cs: builder.Configuration["MongoDb:ConnectionString"] = vaultValue;
            var connectionString = configuration["MongoDb:ConnectionString"];
            var databaseName = configuration["MongoDb:DatabaseName"] ?? "AuktionshusDB"; // Default if not found in config
            var collectionName = configuration["MongoDb:CollectionName"] ?? "Users";     // Default if not found in config

            _logger.LogInformation("UserMongoDBService: Initializing MongoDB connection...");
            // Avoid logging the full connection string for security reasons in production logs.
            // Log a confirmation that a connection string was retrieved.
            if (!string.IsNullOrEmpty(connectionString))
            {
                _logger.LogInformation("UserMongoDBService: MongoDB ConnectionString retrieved (length: {Length}).", connectionString.Length);
            }
            _logger.LogInformation("UserMongoDBService: Using database: {DatabaseName}", databaseName);
            _logger.LogInformation("UserMongoDBService: Using collection: {CollectionName}", collectionName);

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogCritical("UserMongoDBService: MongoDB ConnectionString ('MongoDb:ConnectionString') is missing or empty in configuration. Service cannot connect to database.");
                throw new InvalidOperationException("MongoDB ConnectionString is not configured. Please check Vault and application configuration.");
            }
            if (string.IsNullOrEmpty(databaseName))
            {
                _logger.LogCritical("UserMongoDBService: MongoDB DatabaseName ('MongoDb:DatabaseName') is missing or empty in configuration.");
                throw new InvalidOperationException("MongoDB DatabaseName is not configured.");
            }
            if (string.IsNullOrEmpty(collectionName))
            {
                _logger.LogCritical("UserMongoDBService: MongoDB CollectionName ('MongoDb:CollectionName') is missing or empty in configuration.");
                throw new InvalidOperationException("MongoDB CollectionName is not configured.");
            }


            try
            {
                var client = new MongoClient(connectionString);
                var database = client.GetDatabase(databaseName);
                _userCollection = database.GetCollection<User>(collectionName);
                _logger.LogInformation("UserMongoDBService: Successfully connected to MongoDB and obtained collection '{CollectionName}' from database '{DatabaseName}'.", collectionName, databaseName);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "UserMongoDBService: CRITICAL - Failed to connect to MongoDB or get collection. ConnectionString used (status): {Status}, Database: {DatabaseName}, Collection: {CollectionName}", string.IsNullOrEmpty(connectionString) ? "MISSING/EMPTY" : "PROVIDED", databaseName, collectionName);
                throw; // Rethrow the exception to prevent the service from starting in a broken state
            }
        }

        public async Task<User> CreateUserAsync(User user)
        {
            if (user == null)
            {
                _logger.LogError("CreateUserAsync: Attempted to create a null user object.");
                throw new ArgumentNullException(nameof(user), "User object cannot be null.");
            }

            // Consider if Id should be set here if not already set by the caller,
            // though your User model's Id is Guid and often set before calling create.
            // if (user.Id == Guid.Empty)
            // {
            //     user.Id = Guid.NewGuid();
            // }

            try
            {
                await _userCollection.InsertOneAsync(user);
                _logger.LogInformation("CreateUserAsync: User created with ID: {UserId}, Username: {Username}, Role: {Role}", user.Id, user.Username, user.Role);
                return user;
            }
            catch (MongoWriteException ex) // More specific exception handling
            {
                _logger.LogError(ex, "CreateUserAsync: MongoDB write error while creating user with ID: {UserId}.", user.Id);
                // Check for duplicate key errors if Username should be unique, for example
                if (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
                {
                    throw new InvalidOperationException($"A user with the username '{user.Username}' or ID '{user.Id}' may already exist.", ex);
                }
                throw; // Rethrow if it's another type of write error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateUserAsync: Unexpected error while creating user with ID: {UserId}.", user.Id);
                throw;
            }
        }

        public async Task<User> GetUserByIdAsync(Guid id)
        {
            if (id == Guid.Empty)
            {
                _logger.LogWarning("GetUserByIdAsync: Invalid user ID (empty GUID) provided.");
                // Depending on requirements, you might return null or throw an ArgumentException.
                // Throwing ArgumentException is often better for invalid input.
                throw new ArgumentException("User ID cannot be empty.", nameof(id));
            }

            _logger.LogInformation("GetUserByIdAsync: Retrieving user with ID: {UserId}", id);
            try
            {
                return await _userCollection.Find(u => u.Id == id).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUserByIdAsync: Error while retrieving user with ID: {UserId}.", id);
                throw;
            }
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            _logger.LogInformation("GetAllUsersAsync: Retrieving all users.");
            try
            {
                // The check for _userCollection == null should ideally not be needed if constructor throws on failure.
                if (_userCollection == null)
                {
                    _logger.LogError("GetAllUsersAsync: _userCollection is null. This indicates a severe initialization problem.");
                    throw new InvalidOperationException("User collection is not initialized.");
                }
                return await _userCollection.Find(_ => true).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllUsersAsync: Error while retrieving all users.");
                throw;
            }
        }

        public async Task<User> UpdateUserAsync(Guid id, User updatedUser)
        {
            if (id == Guid.Empty)
            {
                _logger.LogWarning("UpdateUserAsync: Invalid user ID (empty GUID) provided for update.");
                throw new ArgumentException("User ID cannot be empty.", nameof(id));
            }
            if (updatedUser == null)
            {
                _logger.LogError("UpdateUserAsync: Attempted to update user with ID: {UserId} using a null updatedUser object.", id);
                throw new ArgumentNullException(nameof(updatedUser), "Updated user object cannot be null.");
            }
            // Ensure the ID in the updatedUser object matches the ID parameter, or is ignored by ReplaceOneAsync.
            // It's good practice to set it to prevent accidental ID changes if ReplaceOneAsync uses the object's Id.
            updatedUser.Id = id;

            _logger.LogInformation("UpdateUserAsync: Attempting to update user with ID: {UserId}", id);
            try
            {
                var result = await _userCollection.ReplaceOneAsync(u => u.Id == id, updatedUser);

                if (result.MatchedCount == 0)
                {
                    _logger.LogWarning("UpdateUserAsync: User with ID: {UserId} not found for update.", id);
                    return null; // Or throw a specific "NotFoundException"
                }
                if (result.ModifiedCount == 0 && result.MatchedCount == 1)
                {
                    _logger.LogInformation("UpdateUserAsync: User with ID: {UserId} was found, but no changes were made (data might be identical).", id);
                }
                else if (result.ModifiedCount > 0)
                {
                    _logger.LogInformation("UpdateUserAsync: User with ID: {UserId} updated successfully.", id);
                }
                return updatedUser; // Return the updated user object as passed in (since ReplaceOneAsync doesn't return the modified doc directly)
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateUserAsync: Error while updating user with ID: {UserId}.", id);
                throw;
            }
        }

        public async Task<bool> DeleteUserAsync(Guid id)
        {
            if (id == Guid.Empty)
            {
                _logger.LogWarning("DeleteUserAsync: Invalid user ID (empty GUID) provided for deletion.");
                throw new ArgumentException("User ID cannot be empty.", nameof(id));
            }

            _logger.LogInformation("DeleteUserAsync: Attempting to delete user with ID: {UserId}", id);
            try
            {
                var result = await _userCollection.DeleteOneAsync(u => u.Id == id);

                if (result.DeletedCount == 0)
                {
                    _logger.LogWarning("DeleteUserAsync: User with ID: {UserId} not found for deletion.", id);
                    return false;
                }

                _logger.LogInformation("DeleteUserAsync: User with ID: {UserId} deleted successfully.", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteUserAsync: Error while deleting user with ID: {UserId}.", id);
                throw;
            }
        }
    }
}