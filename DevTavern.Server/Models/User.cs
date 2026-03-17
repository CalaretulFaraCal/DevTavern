using System.ComponentModel.DataAnnotations;

namespace DevTavern.Server.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string GitHubId { get; set; } = string.Empty;

        [Required]
        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<Project> Projects { get; set; } = new List<Project>();
    }
}
