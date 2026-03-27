using Xunit;
using Moq;
using DevTavern.Server.Controllers;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace DevTavern.Tests
{
    public class UsersControllerTests
    {
        private readonly Mock<IRepository<User>> _mockUserRepo;
        private readonly UsersController _controller;

        public UsersControllerTests()
        {
            _mockUserRepo = new Mock<IRepository<User>>();
            _controller = new UsersController(_mockUserRepo.Object);
        }

        // =====================================================
        // TEST: GET /api/users — Returnează toți utilizatorii
        // =====================================================

        [Fact]
        public async Task GetUsers_ReturnsOkResult_WithListOfUsers()
        {
            // Arrange — Simulăm 2 useri în baza de date
            var fakeUsers = new List<User>
            {
                new User { Id = 1, GitHubId = "gh001", Username = "Alice" },
                new User { Id = 2, GitHubId = "gh002", Username = "Bob" }
            };
            _mockUserRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(fakeUsers);

            // Act — Apelăm endpoint-ul
            var result = await _controller.GetUsers();

            // Assert — Verificăm că primim OK + lista corectă
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var users = Assert.IsAssignableFrom<IEnumerable<User>>(okResult.Value);
            Assert.Equal(2, users.Count());
        }

        [Fact]
        public async Task GetUsers_ReturnsEmptyList_WhenNoUsersExist()
        {
            // Arrange — Baza de date goală
            _mockUserRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<User>());

            // Act
            var result = await _controller.GetUsers();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var users = Assert.IsAssignableFrom<IEnumerable<User>>(okResult.Value);
            Assert.Empty(users);
        }

        // =====================================================
        // TEST: GET /api/users/{id} — Returnează un user by ID
        // =====================================================

        [Fact]
        public async Task GetUserById_ReturnsOk_WhenUserExists()
        {
            // Arrange
            var fakeUser = new User { Id = 1, GitHubId = "gh001", Username = "Alice" };
            _mockUserRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(fakeUser);

            // Act
            var result = await _controller.GetUserById(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var user = Assert.IsType<User>(okResult.Value);
            Assert.Equal("Alice", user.Username);
        }

        [Fact]
        public async Task GetUserById_ReturnsNotFound_WhenUserDoesNotExist()
        {
            // Arrange — ID-ul 999 nu există
            _mockUserRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((User?)null);

            // Act
            var result = await _controller.GetUserById(999);

            // Assert — Trebuie să primim NotFound (404)
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        // =====================================================
        // TEST: POST /api/users — Crearea unui user nou
        // =====================================================

        [Fact]
        public async Task CreateUser_ReturnsCreatedResult_WithValidUser()
        {
            // Arrange
            var newUser = new User { Id = 10, GitHubId = "gh010", Username = "Charlie" };

            // Act
            var result = await _controller.CreateUser(newUser);

            // Assert — Așteptăm 201 Created
            var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            var user = Assert.IsType<User>(createdResult.Value);
            Assert.Equal("Charlie", user.Username);

            // Verificăm că AddAsync a fost apelat o singură dată
            _mockUserRepo.Verify(r => r.AddAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task CreateUser_ReturnsBadRequest_WhenUserIsNull()
        {
            // Act
            var result = await _controller.CreateUser(null!);

            // Assert — Așteptăm 400 Bad Request
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }
}
