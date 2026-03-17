using System.ComponentModel.DataAnnotations;

namespace DevTavern.Server.Models
{
    public class Project
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string GitHubRepoId { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public ICollection<Channel> Channels { get; set; } = new List<Channel>();
        public ICollection<User> Members { get; set; } = new List<User>();
    }
}
