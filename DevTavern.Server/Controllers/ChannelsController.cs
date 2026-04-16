using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using DevTavern.Server.Factories;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevTavern.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChannelsController : ControllerBase
    {
        private readonly IRepository<Channel> _channelRepository;
        private readonly IRepository<Message> _messageRepository;
        private readonly IChannelFactory _channelFactory;

        public ChannelsController(IRepository<Channel> channelRepository, IRepository<Message> messageRepository, IChannelFactory channelFactory)
        {
            _channelRepository = channelRepository;
            _messageRepository = messageRepository;
            _channelFactory = channelFactory;
        }

        // GET /api/channels
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Channel>>> GetChannels()
        {
            var channels = await _channelRepository.GetAllAsync();
            return Ok(channels);
        }

        // POST /api/channels/generate-defaults/{projectId} - Factory Pattern: genereaza 2 canale default
        [HttpPost("generate-defaults/{projectId}")]
        public async Task<ActionResult<IEnumerable<Channel>>> GenerateDefaultChannels(int projectId)
        {
            var allChannels = await _channelRepository.GetAllAsync();
            var existingChannels = allChannels.Where(c => c.ProjectId == projectId).ToList();

            if (existingChannels.Any())
            {
                // Dacă baza de date zice că grupul ăsta e deschis și are deja canale... le dăm DOAR pe ele, fără a le recrea duplicat!
                return Ok(existingChannels);
            }

            var techChannel = _channelFactory.CreateProjectChannel(projectId);
            var loungeChannel = _channelFactory.CreateOffTopicChannel(projectId);

            await _channelRepository.AddAsync(techChannel);
            await _channelRepository.AddAsync(loungeChannel);

            var generatedChannels = new List<Channel> { techChannel, loungeChannel };
            
            return Ok(generatedChannels);
        }

        // POST /api/channels - creeaza un canal custom
        [HttpPost]
        public async Task<ActionResult<Channel>> CreateCustomChannel(Channel customChannel)
        {
            if (customChannel == null) return BadRequest();

            await _channelRepository.AddAsync(customChannel);
            
            return CreatedAtAction(nameof(GetChannels), new { id = customChannel.Id }, customChannel);
        }

        // POST /api/channels/project/{projectId} - Metodă directă ca să adaugi clar canale într-un anume grup
        [HttpPost("project/{projectId}")]
        public async Task<ActionResult<Channel>> AddChannelToGroup(int projectId, [FromBody] Channel customChannel)
        {
            if (string.IsNullOrWhiteSpace(customChannel.Name)) 
                return BadRequest("Numele canalului este obligatoriu.");

            // Indiferent ce id vine in JSON, forțăm canalul să aparțină Grupului ales sus
            customChannel.ProjectId = projectId;
            
            await _channelRepository.AddAsync(customChannel);
            return Ok(customChannel);
        }
        // DELETE /api/channels/{id} - sterge un canal
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteChannel(int id)
        {
            var allChannels = await _channelRepository.GetAllAsync();
            var channelToDelete = allChannels.FirstOrDefault(c => c.Id == id);
            
            if (channelToDelete == null) return NotFound();

            // Stergem toate mesajele asociate
            var allMessages = await _messageRepository.GetAllAsync();
            var relatedMessages = allMessages.Where(m => m.ChannelId == id).ToList();

            foreach (var msg in relatedMessages)
            {
                _messageRepository.Delete(msg);
            }
            await _messageRepository.SaveChangesAsync();

            _channelRepository.Delete(channelToDelete);
            await _channelRepository.SaveChangesAsync();
            return NoContent();
        }
    }
}
