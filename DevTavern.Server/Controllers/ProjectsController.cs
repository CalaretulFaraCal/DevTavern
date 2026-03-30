using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

namespace DevTavern.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectsController : ControllerBase
    {
        private readonly IRepository<Project> _projectRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public ProjectsController(IRepository<Project> projectRepository, IHttpClientFactory httpClientFactory)
        {
            _projectRepository = projectRepository;
            _httpClientFactory = httpClientFactory;
        }

        // GET /api/projects
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Project>>> GetProjects()
        {
            var projects = await _projectRepository.GetAllAsync();
            return Ok(projects);
        }

        // GET /api/projects/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Project>> GetProject(int id)
        {
            var project = await _projectRepository.GetByIdAsync(id);
            if (project == null) return NotFound();
            return Ok(project);
        }

        // POST /api/projects
        [HttpPost]
        public async Task<ActionResult<Project>> CreateProject(Project newProject)
        {
            if (newProject == null) return BadRequest();

            // Verificare duplicat (Cerere: "verificare daca grupul exista deja")
            var allProjects = await _projectRepository.GetAllAsync();
            var existenta = allProjects.FirstOrDefault(p => p.Name == newProject.Name);

            if (existenta != null)
            {
                // În loc de mesaj teoretic de eroare Backend, returnăm pur și simplu proiectul deja existent ca Front-End-ul să intre în el fluent!
                return Ok(existenta);
            }

            await _projectRepository.AddAsync(newProject);
            
            return CreatedAtAction(nameof(GetProject), new { id = newProject.Id }, newProject);
        }

        // GET /api/projects/github/my-projects - aduce repo-urile GitHub (filtrate, doar ce e util)
        [HttpGet("github/my-projects")]
        public async Task<IActionResult> GetMyGitHubRepositories([FromQuery] string githubPersonalAccessToken)
        {
            if (string.IsNullOrWhiteSpace(githubPersonalAccessToken))
                return BadRequest("Avem nevoie de token-ul tău de pe GitHub pentru a aduce proiectele private.");

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Universitate");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {githubPersonalAccessToken}");

            var response = await client.GetAsync($"https://api.github.com/user/repos?type=all");
            
            if (!response.IsSuccessStatusCode)
            {
                return BadRequest($"Eroare GitHub API. Token-ul este greșit sau invalid.");
            }

            var content = await response.Content.ReadAsStringAsync();
            var repos = JsonSerializer.Deserialize<JsonElement>(content);

            // Filtrăm doar câmpurile utile din răspunsul GitHub
            var filteredRepos = new List<object>();
            foreach (var repo in repos.EnumerateArray())
            {
                filteredRepos.Add(new
                {
                    id = repo.GetProperty("id").GetInt64(),
                    name = repo.GetProperty("name").GetString(),
                    fullName = repo.GetProperty("full_name").GetString(),
                    isPrivate = repo.GetProperty("private").GetBoolean(),
                    owner = repo.GetProperty("owner").GetProperty("login").GetString(),
                    description = repo.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    language = repo.TryGetProperty("language", out var lang) ? lang.GetString() : null,
                    url = repo.GetProperty("html_url").GetString(),
                    createdAt = repo.GetProperty("created_at").GetString(),
                    updatedAt = repo.GetProperty("updated_at").GetString()
                });
            }

            return Ok(filteredRepos);
        }
    }
}