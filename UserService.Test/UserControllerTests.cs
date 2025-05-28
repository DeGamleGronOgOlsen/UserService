using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Services;
using UserService.Controllers;
using UserService.Models;

namespace UserService.Test.Controllers
{
    [TestFixture]
    public class UserControllerTests
    {
        private UserController _controller = null!;
        private Mock<IUserDBRepository> _mockRepository = null!;
        private Mock<ILogger<UserController>> _mockLogger = null!;

        [SetUp]
        public void Setup()
        {
            _mockRepository = new Mock<IUserDBRepository>();
            _mockLogger = new Mock<ILogger<UserController>>();
            _controller = new UserController(_mockRepository.Object, _mockLogger.Object);
        }

        [Test]
        public async Task AddUser_WithValidData_ReturnsCreatedResponse()
        {
            // Arrange
            var newUser = new User
            {
                Username = "testuser",
                Password = "testpass",
                Role = Role.User  // Changed from string to enum
            };

            _mockRepository.Setup(r => r.CreateUserAsync(It.IsAny<User>()))
                .ReturnsAsync(newUser);

            // Act
            var result = await _controller.AddUser(newUser) as CreatedAtRouteResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(201));
            Assert.That(result.Value, Is.EqualTo(newUser));
        }

        [Test]
        public async Task ValidateUser_WithValidCredentials_ReturnsOkWithRole()
        {
            // Arrange
            var login = new LoginModel
            {
                Username = "testuser",
                Password = "testpass"
            };

            var users = new List<User> 
            { 
                new User 
                { 
                    Username = "testuser",
                    Password = "testpass",
                    Role = Role.Admin
                }
            };

            _mockRepository.Setup(r => r.GetAllUsersAsync())
                .ReturnsAsync(users);

            // Act
            var result = await _controller.ValidateUser(login) as OkObjectResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(200));
            Assert.That(result.Value, Is.Not.Null);

            // Create a type to match the anonymous type returned by the controller
            var response = result.Value.GetType().GetProperty("Role").GetValue(result.Value);
            Assert.That(response.ToString(), Is.EqualTo(Role.Admin.ToString()));
        }

        [Test]
        public async Task DeleteUser_WithExistingId_ReturnsNoContent()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var existingUser = new User { Id = userId };

            _mockRepository.Setup(r => r.GetUserByIdAsync(userId))
                .ReturnsAsync(existingUser);
            _mockRepository.Setup(r => r.DeleteUserAsync(userId))
                .ReturnsAsync(true);

            // Act
            var result = await _controller.DeleteUser(userId) as NoContentResult;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(204));
        }
    }
}