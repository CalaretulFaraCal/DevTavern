using System.ComponentModel.DataAnnotations;

namespace DevTavern.Server.Models
{
    public enum ChannelType
    {
        Project,
        OffTopic
    }

    public class Channel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public ChannelType Type { get; set; }

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
