using Xunit;
using Moq;
using DevTavern.Server.Controllers;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using DevTavern.Server.Factories;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace DevTavern.Tests
{
    public class ChannelsControllerTests
    {
        private readonly Mock<IRepository<Channel>> _mockChannelRepo;
        private readonly Mock<IChannelFactory> _mockChannelFactory;
        private readonly ChannelsController _controller;

        public ChannelsControllerTests()
        {
            _mockChannelRepo = new Mock<IRepository<Channel>>();
            _mockChannelFactory = new Mock<IChannelFactory>();
            _controller = new ChannelsController(_mockChannelRepo.Object, _mockChannelFactory.Object);
        }

        // =====================================================
        // TEST: GET /api/channels — Lista tuturor canalelor
        // =====================================================

        [Fact]
        public async Task GetChannels_ReturnsOk_WithAllChannels()
        {
            // Arrange
            var fakeChannels = new List<Channel>
            {
                new Channel { Id = 1, Name = "general-tech", Type = ChannelType.Project, ProjectId = 1 },
                new Channel { Id = 2, Name = "off-topic-lounge", Type = ChannelType.OffTopic, ProjectId = 1 }
            };
            _mockChannelRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(fakeChannels);

            // Act
            var result = await _controller.GetChannels();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var channels = Assert.IsAssignableFrom<IEnumerable<Channel>>(okResult.Value);
            Assert.Equal(2, channels.Count());
        }

        // =================================================================
        // TEST: POST /api/channels/generate-defaults/{projectId}
        // Verificăm că Factory Pattern generează corect cele 2 canale
        // =================================================================

        [Fact]
        public async Task GenerateDefaultChannels_CreatesTwoChannels()
        {
            // Arrange — Configurăm fabrica să returneze canale simulate
            int projectId = 5;
            var techChannel = new Channel { Name = "general-tech", Type = ChannelType.Project, ProjectId = projectId };
            var offTopicChannel = new Channel { Name = "off-topic-lounge", Type = ChannelType.OffTopic, ProjectId = projectId };

            _mockChannelFactory.Setup(f => f.CreateProjectChannel(projectId)).Returns(techChannel);
            _mockChannelFactory.Setup(f => f.CreateOffTopicChannel(projectId)).Returns(offTopicChannel);

            // Act
            var result = await _controller.GenerateDefaultChannels(projectId);

            // Assert — Verificăm că s-au creat exact 2 canale
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var channels = Assert.IsAssignableFrom<List<Channel>>(okResult.Value);
            Assert.Equal(2, channels.Count);

            // Verificăm tipurile canalelor
            Assert.Contains(channels, c => c.Type == ChannelType.Project);
            Assert.Contains(channels, c => c.Type == ChannelType.OffTopic);

            // Verificăm că AddAsync s-a apelat de 2 ori (odată pentru fiecare canal)
            _mockChannelRepo.Verify(r => r.AddAsync(It.IsAny<Channel>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GenerateDefaultChannels_UsesCorrectProjectId()
        {
            // Arrange
            int projectId = 42;
            _mockChannelFactory.Setup(f => f.CreateProjectChannel(projectId))
                .Returns(new Channel { Name = "general-tech", Type = ChannelType.Project, ProjectId = projectId });
            _mockChannelFactory.Setup(f => f.CreateOffTopicChannel(projectId))
                .Returns(new Channel { Name = "off-topic-lounge", Type = ChannelType.OffTopic, ProjectId = projectId });

            // Act
            var result = await _controller.GenerateDefaultChannels(projectId);

            // Assert — Toate canalele trebuie să aibă ProjectId = 42
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var channels = Assert.IsAssignableFrom<List<Channel>>(okResult.Value);
            Assert.All(channels, c => Assert.Equal(42, c.ProjectId));
        }

        // =================================================================
        // TEST: POST /api/channels — Creare canal custom
        // =================================================================

        [Fact]
        public async Task CreateCustomChannel_ReturnsCreated_WithValidChannel()
        {
            // Arrange
            var customChannel = new Channel { Id = 10, Name = "debug-room", Type = ChannelType.Project, ProjectId = 1 };

            // Act
            var result = await _controller.CreateCustomChannel(customChannel);

            // Assert
            var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            var channel = Assert.IsType<Channel>(createdResult.Value);
            Assert.Equal("debug-room", channel.Name);
        }

        [Fact]
        public async Task CreateCustomChannel_ReturnsBadRequest_WhenNull()
        {
            // Act
            var result = await _controller.CreateCustomChannel(null!);

            // Assert
            Assert.IsType<BadRequestResult>(result.Result);
        }
    }
}
