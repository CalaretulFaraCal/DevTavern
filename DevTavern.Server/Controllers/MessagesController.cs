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

        // Dependency Injection pentru Mesaje
        public MessagesController(IRepository<Message> messageRepository)
        {
            _messageRepository = messageRepository;
        }

        // Endpoint GET /api/messages/channel/{channelId}
        // Aducem istoricul de mesaje doar pentru camera (canalul) in care a intrat utilizatorul
        [HttpGet("channel/{channelId}")]
        public async Task<ActionResult<IEnumerable<Message>>> GetMessagesForChannel(int channelId)
        {
            var allMessages = await _messageRepository.GetAllAsync();
            
            // Filtram prin interogare LINQ ca sa aducem doar mesajele canalului dorit, ordonate dupa timp
            var channelMessages = allMessages
                .Where(m => m.ChannelId == channelId)
                .OrderBy(m => m.SentAt)
                .ToList();

            return Ok(channelMessages);
        }

        // Endpoint POST /api/messages
        // Adaugam un mesaj in baza de date cand utilizatorul da "Send"
        [HttpPost]
        public async Task<ActionResult<Message>> SendMessage(Message newMessage)
        {
            if (string.IsNullOrWhiteSpace(newMessage.Content))
            {
                return BadRequest("Mesajul nu poate fi gol!");
            }

            // Punem data si ora exacta la care s-a inregistrat in server
            newMessage.SentAt = DateTime.UtcNow;

            await _messageRepository.AddAsync(newMessage);

            return Ok(newMessage);
        }
    }
}
