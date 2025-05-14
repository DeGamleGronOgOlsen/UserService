using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UserService.Models;

namespace Services;
public interface IUserDBRepository
{
Task<User> CreateUserAsync(User user);
Task<User> GetUserByIdAsync(Guid id);
Task<IEnumerable<User>> GetAllUsersAsync();
Task<User> UpdateUserAsync(Guid id, User updatedUser);
Task<bool> DeleteUserAsync(Guid id);
}