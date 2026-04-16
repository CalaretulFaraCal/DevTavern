using System.ComponentModel.DataAnnotations;

namespace DevTavern.Server.Models
{
    public class ProjectRoleMapping
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string Username { get; set; } = string.Empty;
        
        public int ProjectId { get; set; }
        
        public string DevRoles { get; set; } = string.Empty; // Comma-separated roles
    }
}
