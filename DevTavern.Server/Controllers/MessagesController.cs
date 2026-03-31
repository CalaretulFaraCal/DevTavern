using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using DevTavern.Server.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace DevTavern.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController : ControllerBase
    {
        private readonly IRepository<Message> _messageRepository;
        private readonly ApplicationDbContext _context;

        public MessagesController(IRepository<Message> messageRepository, ApplicationDbContext context)
        {
            _messageRepository = messageRepository;
            _context = context;
        }

        // GET /api/messages/channel/{channelId} - mesajele unui canal, ordonate cronologic
        [HttpGet("channel/{channelId}")]
        public async Task<ActionResult<IEnumerable<Message>>> GetMessagesForChannel(int channelId)
        {
            var channelMessages = await _context.Messages
                .Include(m => m.User)
                .Where(m => m.ChannelId == channelId)
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            return Ok(channelMessages);
        }

        // POST /api/messages - trimite un mesaj nou
        [HttpPost]
        public async Task<ActionResult<Message>> SendMessage(Message newMessage)
        {
            if (string.IsNullOrWhiteSpace(newMessage.Content))
            {
                return BadRequest("Mesajul nu poate fi gol!");
            }

            newMessage.SentAt = DateTime.UtcNow;

            await _messageRepository.AddAsync(newMessage);

            return Ok(newMessage);
        }
    }
}

