using Xunit;
using DevTavern.Server.Models;
using System.Collections.Generic;
using System.Linq;

namespace DevTavern.Tests
{
    public class UserTests
    {
        [Fact]
        public void User_CanBeAssignedToProjects()
        {
            // Arrange
            var user = new User() { Id = 1, GitHubId = "git123", Username = "TestUser" };
            var project = new Project() { Id = 1, GitHubRepoId = "repo123", Name = "TestRepo" };

            // Act
            user.Projects.Add(project);

            // Assert
            Assert.Single(user.Projects);
            Assert.Equal("TestRepo", user.Projects.First().Name);
        }
    }
}