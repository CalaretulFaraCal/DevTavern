using System.ComponentModel.DataAnnotations;

namespace DevTavern.Server.Models
{
    public class Message
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int ChannelId { get; set; }
        public Channel Channel { get; set; } = null!;
    }
}
