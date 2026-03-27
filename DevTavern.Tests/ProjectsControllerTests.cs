using Xunit;
using Moq;
using DevTavern.Server.Controllers;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Http;
using Moq.Protected;
using System.Threading;

namespace DevTavern.Tests
{
    public class ProjectsControllerTests
    {
        private readonly Mock<IRepository<Project>> _mockProjectRepo;
        private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
        private readonly ProjectsController _controller;

        public ProjectsControllerTests()
        {
            _mockProjectRepo = new Mock<IRepository<Project>>();
            _mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _controller = new ProjectsController(_mockProjectRepo.Object, _mockHttpClientFactory.Object);
        }

        // =====================================================
        // TEST: GET /api/projects — Lista tuturor proiectelor
        // =====================================================

        [Fact]
        public async Task GetProjects_ReturnsOk_WithAllProjects()
        {
            // Arrange
            var fakeProjects = new List<Project>
            {
                new Project { Id = 1, GitHubRepoId = "repo1", Name = "DevTavern" },
                new Project { Id = 2, GitHubRepoId = "repo2", Name = "AltProiect" }
            };
            _mockProjectRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(fakeProjects);

            // Act
            var result = await _controller.GetProjects();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var projects = Assert.IsAssignableFrom<IEnumerable<Project>>(okResult.Value);
            Assert.Equal(2, projects.Count());
        }

        // =====================================================
        // TEST: GET /api/projects/{id} — Un proiect by ID
        // =====================================================

        [Fact]
        public async Task GetProject_ReturnsOk_WhenProjectExists()
        {
            // Arrange
            var fakeProject = new Project { Id = 1, GitHubRepoId = "repo1", Name = "DevTavern" };
            _mockProjectRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(fakeProject);

            // Act
            var result = await _controller.GetProject(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var project = Assert.IsType<Project>(okResult.Value);
            Assert.Equal("DevTavern", project.Name);
        }

        [Fact]
        public async Task GetProject_ReturnsNotFound_WhenProjectMissing()
        {
            // Arrange
            _mockProjectRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Project?)null);

            // Act
            var result = await _controller.GetProject(999);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        // =====================================================
        // TEST: POST /api/projects — Adăugare proiect
        // =====================================================

        [Fact]
        public async Task CreateProject_ReturnsCreated_WithValidProject()
        {
            // Arrange
            var newProject = new Project { Id = 3, GitHubRepoId = "repo3", Name = "ProiectNou" };

            // Act
            var result = await _controller.CreateProject(newProject);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            var project = Assert.IsType<Project>(createdResult.Value);
            Assert.Equal("ProiectNou", project.Name);
            _mockProjectRepo.Verify(r => r.AddAsync(It.IsAny<Project>()), Times.Once);
        }

        [Fact]
        public async Task CreateProject_ReturnsBadRequest_WhenNull()
        {
            // Act
            var result = await _controller.CreateProject(null!);

            // Assert
            Assert.IsType<BadRequestResult>(result.Result);
        }
    }
}
