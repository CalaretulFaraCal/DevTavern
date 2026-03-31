using Xunit;
using Moq;
using DevTavern.Server.Controllers;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using DevTavern.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace DevTavern.Tests
{
    public class MessagesControllerTests
    {
        private readonly Mock<IRepository<Message>> _mockMessageRepo;

        public MessagesControllerTests()
        {
            _mockMessageRepo = new Mock<IRepository<Message>>();
        }

        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        // =====================================================
        // TEST: GET /api/messages/channel/{channelId}
        // Verificăm că mesajele sunt filtrate corect pe canal
        // =====================================================

        [Fact]
        public async Task GetMessagesForChannel_ReturnsOnlyMessagesFromThatChannel()
        {
            // Arrange — 3 mesaje și utilizatorii lor lipsă
            var context = CreateInMemoryDbContext();
            context.Users.AddRange(
                new User { Id = 1, Username = "User1", GitHubId = "gh1" },
                new User { Id = 2, Username = "User2", GitHubId = "gh2" }
            );
            context.Messages.AddRange(
                new Message { Id = 1, Content = "Salut!", ChannelId = 1, UserId = 1, SentAt = DateTime.UtcNow.AddMinutes(-10) },
                new Message { Id = 2, Content = "Ce faci?", ChannelId = 1, UserId = 2, SentAt = DateTime.UtcNow.AddMinutes(-5) },
                new Message { Id = 3, Content = "Alt canal", ChannelId = 2, UserId = 1, SentAt = DateTime.UtcNow }
            );
            await context.SaveChangesAsync();
            
            var controller = new MessagesController(_mockMessageRepo.Object, context);

            // Act — Cerem mesajele doar pentru canalul 1
            var result = await controller.GetMessagesForChannel(1);

            // Assert — Ar trebui să primim doar 2 mesaje
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var messages = Assert.IsAssignableFrom<List<Message>>(okResult.Value);
            Assert.Equal(2, messages.Count);
            Assert.All(messages, m => Assert.Equal(1, m.ChannelId));
        }

        [Fact]
        public async Task GetMessagesForChannel_ReturnsMessagesOrderedByTime()
        {
            // Arrange — Mesaje în ordine inversă + conturi user test
            var context = CreateInMemoryDbContext();
            context.Users.AddRange(
                new User { Id = 1, Username = "Testu", GitHubId = "test_g1" },
                new User { Id = 2, Username = "TestuDoi", GitHubId = "test_g2" }
            );
            context.Messages.AddRange(
                new Message { Id = 1, Content = "Al doilea", ChannelId = 1, UserId = 1, SentAt = DateTime.UtcNow },
                new Message { Id = 2, Content = "Primul", ChannelId = 1, UserId = 2, SentAt = DateTime.UtcNow.AddMinutes(-30) }
            );
            await context.SaveChangesAsync();

            var controller = new MessagesController(_mockMessageRepo.Object, context);

            // Act
            var result = await controller.GetMessagesForChannel(1);

            // Assert — Primul mesaj cronologic trebuie să fie "Primul"
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var messages = Assert.IsAssignableFrom<List<Message>>(okResult.Value);
            Assert.Equal("Primul", messages.First().Content);
            Assert.Equal("Al doilea", messages.Last().Content);
        }

        [Fact]
        public async Task GetMessagesForChannel_ReturnsEmpty_WhenNoMessages()
        {
            // Arrange — Niciun mesaj pe canalul 99
            var context = CreateInMemoryDbContext();
            var controller = new MessagesController(_mockMessageRepo.Object, context);

            // Act
            var result = await controller.GetMessagesForChannel(99);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var messages = Assert.IsAssignableFrom<List<Message>>(okResult.Value);
            Assert.Empty(messages);
        }

        // =====================================================
        // TEST: POST /api/messages — Trimiterea unui mesaj
        // =====================================================

        [Fact]
        public async Task SendMessage_ReturnsOk_WithValidMessage()
        {
            // Arrange
            var context = CreateInMemoryDbContext();
            var controller = new MessagesController(_mockMessageRepo.Object, context);
            var newMessage = new Message { Content = "Hello world!", ChannelId = 1, UserId = 1 };

            // Act
            var result = await controller.SendMessage(newMessage);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var message = Assert.IsType<Message>(okResult.Value);
            Assert.Equal("Hello world!", message.Content);

            // Verificăm că SentAt a fost setat automat
            Assert.True(message.SentAt <= DateTime.UtcNow);
            Assert.True(message.SentAt > DateTime.UtcNow.AddSeconds(-5));

            // Verificăm că mesajul a fost salvat
            _mockMessageRepo.Verify(r => r.AddAsync(It.IsAny<Message>()), Times.Once);
        }

        [Fact]
        public async Task SendMessage_ReturnsBadRequest_WhenContentIsEmpty()
        {
            // Arrange — Mesaj gol (trebuie blocat conform TestPlans T-007)
            var context = CreateInMemoryDbContext();
            var controller = new MessagesController(_mockMessageRepo.Object, context);
            var emptyMessage = new Message { Content = "", ChannelId = 1, UserId = 1 };

            // Act
            var result = await controller.SendMessage(emptyMessage);

            // Assert — Nu se permite trimiterea
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task SendMessage_ReturnsBadRequest_WhenContentIsWhitespace()
        {
            // Arrange — Mesaj cu doar spații
            var context = CreateInMemoryDbContext();
            var controller = new MessagesController(_mockMessageRepo.Object, context);
            var whitespaceMessage = new Message { Content = "   ", ChannelId = 1, UserId = 1 };

            // Act
            var result = await controller.SendMessage(whitespaceMessage);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result.Result);
        }
    }
}
