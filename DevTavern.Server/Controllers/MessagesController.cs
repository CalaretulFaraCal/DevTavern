using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
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

        public MessagesController(IRepository<Message> messageRepository)
        {
            _messageRepository = messageRepository;
        }

        // GET /api/messages/channel/{channelId} - mesajele unui canal, ordonate cronologic
        [HttpGet("channel/{channelId}")]
        public async Task<ActionResult<IEnumerable<Message>>> GetMessagesForChannel(int channelId)
        {
            var allMessages = await _messageRepository.GetAllAsync();
            
            var channelMessages = allMessages
                .Where(m => m.ChannelId == channelId)
                .OrderBy(m => m.SentAt)
                .ToList();

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
