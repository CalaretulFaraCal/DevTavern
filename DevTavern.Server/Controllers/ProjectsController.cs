using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;

namespace DevTavern.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectsController : ControllerBase
    {
        private readonly IRepository<Project> _projectRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        // Dependency Injection extins: cerem si "fabrica de browser" pentru a lua date de pe net
        public ProjectsController(IRepository<Project> projectRepository, IHttpClientFactory httpClientFactory)
        {
            _projectRepository = projectRepository;
            _httpClientFactory = httpClientFactory;
        }

        // Endpoint GET /api/projects
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Project>>> GetProjects()
        {
            var projects = await _projectRepository.GetAllAsync();
            return Ok(projects);
        }

        // Endpoint GET /api/projects/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Project>> GetProject(int id)
        {
            var project = await _projectRepository.GetByIdAsync(id);
            if (project == null) return NotFound();
            return Ok(project);
        }

        // Endpoint POST /api/projects
        // Metoda prin care se adauga un proiect/repository nou de pe GitHub in aplicatia noastra
        [HttpPost]
        public async Task<ActionResult<Project>> CreateProject(Project newProject)
        {
            if (newProject == null) return BadRequest();

            await _projectRepository.AddAsync(newProject);
            
            return CreatedAtAction(nameof(GetProject), new { id = newProject.Id }, newProject);
        }

        // Endpoint GET /api/projects/github/my-projects
        // [CERINTA 11] Cauta pe internet (API-ul public GitHub) lista TUTUROR Repo-urilor (inclusiv cele private sau cu colaboratori)
        [HttpGet("github/my-projects")]
        public async Task<IActionResult> GetMyGitHubRepositories([FromQuery] string githubPersonalAccessToken)
        {
            if (string.IsNullOrWhiteSpace(githubPersonalAccessToken))
                return BadRequest("Avem nevoie de token-ul tău de pe GitHub pentru a aduce proiectele private.");

            var client = _httpClientFactory.CreateClient();
            
            // Ne inregistram ca aplicatie 
            client.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Universitate");
            
            // ATASIAM PAROLA DE LA GITHUB LA CERERE
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {githubPersonalAccessToken}");

            // Facem cerere MAJORA spre serverele GitHub (/user/repos in loc de /users/nume/repos)
            // Acest link trage absolut tot ce vezi cand te uiti in propriul cont.
            var response = await client.GetAsync($"https://api.github.com/user/repos?type=all");
            
            if (!response.IsSuccessStatusCode)
            {
                return BadRequest($"Eroare GitHub API. Token-ul este greșit sau invalid.");
            }

            var content = await response.Content.ReadAsStringAsync();
            return Content(content, "application/json");
        }
    }
}
