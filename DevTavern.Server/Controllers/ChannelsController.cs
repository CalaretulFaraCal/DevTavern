using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using DevTavern.Server.Factories;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevTavern.Server.Controllers
{
    // Calea de acces: https://localhost:5114/api/channels
    [ApiController]
    [Route("api/[controller]")]
    public class ChannelsController : ControllerBase
    {
        private readonly IRepository<Channel> _channelRepository;
        private readonly IChannelFactory _channelFactory;

        // Dependency Injection DUBLU (Cerința 10)
        // Aducem atat accesul la Baza de Date cat si "Fabrica" invizibila care stie sa creeze tipuri de retele
        public ChannelsController(IRepository<Channel> channelRepository, IChannelFactory channelFactory)
        {
            _channelRepository = channelRepository;
            _channelFactory = channelFactory;
        }

        // Endpoint GET /api/channels
        // Iti aduce pur si simplu toate canalele pe care le cunoaste baza data
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Channel>>> GetChannels()
        {
            var channels = await _channelRepository.GetAllAsync();
            return Ok(channels);
        }

        // Endpoint POST /api/channels/generate-defaults/{projectId}
        // Aici sclipeste Cerinta 6 (Design Patterns)! Generam instant 2 canale specifice pentru un id de proiect
        [HttpPost("generate-defaults/{projectId}")]
        public async Task<ActionResult<IEnumerable<Channel>>> GenerateDefaultChannels(int projectId)
        {
            // Apelam "Fabrica" ca sa ne construiasca obiectele complexe
            var techChannel = _channelFactory.CreateProjectChannel(projectId);
            var loungeChannel = _channelFactory.CreateOffTopicChannel(projectId);

            // Le salvam in baza de date direct cu Repository-ul nostru generic
            await _channelRepository.AddAsync(techChannel);
            await _channelRepository.AddAsync(loungeChannel);

            // Raspundem aplicatiei client cu listuta de canale nou create!
            var generatedChannels = new List<Channel> { techChannel, loungeChannel };
            
            return Ok(generatedChannels);
        }

        // Endpoint prin care creezi un canal pur Custom (fara sablon de la fabrica)
        [HttpPost]
        public async Task<ActionResult<Channel>> CreateCustomChannel(Channel customChannel)
        {
            if (customChannel == null) return BadRequest();

            await _channelRepository.AddAsync(customChannel);
            
            return CreatedAtAction(nameof(GetChannels), new { id = customChannel.Id }, customChannel);
        }
    }
}
