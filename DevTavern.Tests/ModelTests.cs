using Xunit;
using DevTavern.Server.Models;
using System.Collections.Generic;
using System.Linq;
using System;

namespace DevTavern.Tests
{
    public class ModelTests
    {
        // =====================================================
        // Teste pe Modelul Channel
        // =====================================================

        [Fact]
        public void Channel_DefaultValues_AreCorrect()
        {
            // Arrange & Act
            var channel = new Channel();

            // Assert — Verificăm valorile implicite
            Assert.Equal(string.Empty, channel.Name);
            Assert.Equal(ChannelType.Project, channel.Type); // Enum default = 0 = Project
            Assert.NotNull(channel.Messages);
            Assert.Empty(channel.Messages);
        }

        [Fact]
        public void Channel_CanHoldMultipleMessages()
        {
            // Arrange
            var channel = new Channel { Id = 1, Name = "general-tech", ProjectId = 1 };
            var msg1 = new Message { Id = 1, Content = "Salut!", ChannelId = 1, UserId = 1 };
            var msg2 = new Message { Id = 2, Content = "Buna!", ChannelId = 1, UserId = 2 };

            // Act
            channel.Messages.Add(msg1);
            channel.Messages.Add(msg2);

            // Assert
            Assert.Equal(2, channel.Messages.Count);
        }

        [Theory]
        [InlineData(ChannelType.Project)]
        [InlineData(ChannelType.OffTopic)]
        public void Channel_CanBeSetToAnyType(ChannelType type)
        {
            // Arrange & Act
            var channel = new Channel { Type = type };

            // Assert
            Assert.Equal(type, channel.Type);
        }

        // =====================================================
        // Teste pe Modelul Message
        // =====================================================

        [Fact]
        public void Message_SentAt_HasDefaultValue()
        {
            // Arrange & Act
            var message = new Message();

            // Assert — SentAt trebuie să fie automat setat
            Assert.True(message.SentAt <= DateTime.UtcNow);
            Assert.True(message.SentAt > DateTime.UtcNow.AddSeconds(-5));
        }

        [Fact]
        public void Message_RequiresContent()
        {
            // Arrange & Act
            var message = new Message { Content = "Test mesaj" };

            // Assert
            Assert.Equal("Test mesaj", message.Content);
        }

        // =====================================================
        // Teste pe Modelul Project
        // =====================================================

        [Fact]
        public void Project_CanHaveMultipleChannelsAndMembers()
        {
            // Arrange
            var project = new Project { Id = 1, GitHubRepoId = "repo1", Name = "DevTavern" };
            var channel = new Channel { Id = 1, Name = "general-tech", ProjectId = 1 };
            var member = new User { Id = 1, GitHubId = "gh001", Username = "TestUser" };

            // Act
            project.Channels.Add(channel);
            project.Members.Add(member);

            // Assert
            Assert.Single(project.Channels);
            Assert.Single(project.Members);
            Assert.Equal("TestUser", project.Members.First().Username);
        }

        [Fact]
        public void Project_DefaultCollections_AreEmpty()
        {
            // Arrange & Act
            var project = new Project();

            // Assert
            Assert.NotNull(project.Channels);
            Assert.NotNull(project.Members);
            Assert.Empty(project.Channels);
            Assert.Empty(project.Members);
        }

        // =====================================================
        // Teste pe Modelul User
        // =====================================================

        [Fact]
        public void User_DefaultCollections_AreEmpty()
        {
            // Arrange & Act 
            var user = new User();

            // Assert
            Assert.NotNull(user.Messages);
            Assert.NotNull(user.Projects);
            Assert.Empty(user.Messages);
            Assert.Empty(user.Projects);
        }

        [Fact]
        public void User_AvatarUrl_IsOptional()
        {
            // Arrange & Act — User fără avatar
            var user = new User { Id = 1, GitHubId = "gh001", Username = "Test" };

            // Assert — AvatarUrl poate fi null
            Assert.Null(user.AvatarUrl);
        }

        [Fact]
        public void User_CanHaveMultipleMessages()
        {
            // Arrange
            var user = new User { Id = 1, GitHubId = "gh001", Username = "ChattyUser" };

            // Act
            user.Messages.Add(new Message { Id = 1, Content = "Msg 1", UserId = 1, ChannelId = 1 });
            user.Messages.Add(new Message { Id = 2, Content = "Msg 2", UserId = 1, ChannelId = 1 });
            user.Messages.Add(new Message { Id = 3, Content = "Msg 3", UserId = 1, ChannelId = 2 });

            // Assert
            Assert.Equal(3, user.Messages.Count);
        }
    }
}
