using Microsoft.AspNetCore.Mvc;
using Services;
using System.Linq;
using System.Diagnostics;
using UserService.Models;
using Microsoft.AspNetCore.Authorization;


namespace UserService.Controllers;

[ApiController]
[Route("[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserDBRepository _userRepository;
    private readonly ILogger<UserController> _logger;
    private readonly string _ipaddr;

    public UserController(IUserDBRepository userRepository, ILogger<UserController> logger)
    {
        _userRepository = userRepository;
        _logger = logger;

        var hostName = System.Net.Dns.GetHostName();
        var ips = System.Net.Dns.GetHostAddresses(hostName);
        _ipaddr = ips.First().MapToIPv4().ToString();
        _logger.LogInformation(1, $"UserService responding from {_ipaddr}");
    }

    // GET: /User/{userId}
    [HttpGet("{userId}", Name = "GetUserById")]
    public async Task<ActionResult<User>> Get(Guid userId)
    {
        _logger.LogInformation("Getting user with ID: {UserId}", userId);
        var user = await _userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User with ID: {UserId} not found", userId);
            return NotFound();
        }
        return Ok(user);
    }

    // GET: /User/GetAllUsers
    [HttpGet("GetAllUsers")]
    public async Task<ActionResult<IEnumerable<User>>> GetAll()
    {
        _logger.LogInformation("Getting all users");
        var users = await _userRepository.GetAllUsersAsync();
        return Ok(users);
    }

    // POST: /User/AddUser
    [Authorize(Roles = "admin")]
    [HttpPost("AddUser")]
    public async Task<IActionResult> AddUser([FromBody] User newUser)
    {
        newUser.Id = Guid.NewGuid(); 
        var createdUser = await _userRepository.CreateUserAsync(newUser);
        _logger.LogInformation("Added new user with ID: {UserId}", createdUser.Id);
        return CreatedAtRoute("GetUserById", new { userId = createdUser.Id }, createdUser);
    }

    // PUT: /User/UpdateUser/{userId}
    [HttpPut("UpdateUser/{userId}")]
    public async Task<IActionResult> UpdateUser(Guid userId, [FromBody] User updatedUser)
    {
        var existingUser = await _userRepository.GetUserByIdAsync(userId);
        if (existingUser == null)
        {
            _logger.LogWarning("User with ID: {UserId} not found", userId);
            return NotFound();
        }

        updatedUser.Id = userId; // Ensure the ID remains the same
        var updated = await _userRepository.UpdateUserAsync(userId, updatedUser);
        _logger.LogInformation("Updated user with ID: {UserId}", userId);
        return Ok(updated);
    }

    // DELETE: /User/DeleteUser/{userId}
    [HttpDelete("DeleteUser/{userId}")]
    public async Task<IActionResult> DeleteUser(Guid userId)
    {
        var existingUser = await _userRepository.GetUserByIdAsync(userId);
        if (existingUser == null)
        {
            _logger.LogWarning("User with ID: {UserId} not found", userId);
            return NotFound();
        }

        var deleted = await _userRepository.DeleteUserAsync(userId);
        if (!deleted)
        {
            _logger.LogError("Failed to delete user with ID: {UserId}", userId);
            return StatusCode(500, "Failed to delete user");
        }

        _logger.LogInformation("Deleted user with ID: {UserId}", userId);
        return NoContent();
    }

    [HttpPost("validate")]
public async Task<IActionResult> ValidateUser([FromBody] LoginModel login)
{
    _logger.LogInformation("Validating user with username: {Username}", login.Username);

    // Find brugeren baseret på brugernavn og adgangskode
    var user = await _userRepository.GetAllUsersAsync();
    var validUser = user.FirstOrDefault(u => u.Username == login.Username && u.Password == login.Password);

    if (validUser == null)
    {
        _logger.LogWarning("Invalid username or password for username: {Username}", login.Username);
        return Unauthorized(new { message = "Invalid username or password" });
    }

    _logger.LogInformation("User validated successfully: {Username}", login.Username);
    return Ok(new { Role = validUser.Role });
}

    
}