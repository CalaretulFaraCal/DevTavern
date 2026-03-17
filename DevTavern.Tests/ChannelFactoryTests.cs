using System;
using Xunit;
using DevTavern.Server.Factories;
using DevTavern.Server.Models;

namespace DevTavern.Tests
{
    public class ChannelFactoryTests
    {
        [Fact]
        public void CreateProjectChannel_ShouldReturnProjectChannelType()
        {
            // Arrange
            var factory = new ChannelFactory();
            int projectId = 1;

            // Act
            var channel = factory.CreateProjectChannel(projectId);

            // Assert
            Assert.NotNull(channel);
            Assert.Equal(ChannelType.Project, channel.Type);
            Assert.Equal("general-tech", channel.Name);
            Assert.Equal(projectId, channel.ProjectId);
        }

        [Fact]
        public void CreateOffTopicChannel_ShouldReturnOffTopicChannelType()
        {
            // Arrange
            var factory = new ChannelFactory();
            int projectId = 5;

            // Act
            var channel = factory.CreateOffTopicChannel(projectId);

            // Assert
            Assert.NotNull(channel);
            Assert.Equal(ChannelType.OffTopic, channel.Type);
            Assert.Equal("off-topic-lounge", channel.Name);
            Assert.Equal(projectId, channel.ProjectId);
        }
    }
}
