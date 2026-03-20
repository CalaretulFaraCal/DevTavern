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

        // Endpoint GET /api/projects/github/{username}
        // [CERINTA 11] Cauta pe internet (API-ul public GitHub) lista de Repository-uri ale unui anume cont
        [HttpGet("github/{username}")]
        public async Task<IActionResult> GetGitHubRepositories(string username)
        {
            var client = _httpClientFactory.CreateClient();
            
            // GitHub cere obligatoriu un 'User-Agent', altfel ne respinge cererea
            client.DefaultRequestHeaders.Add("User-Agent", "DevTavern-Universitate");

            // Trimitem cererea HTTP adevarata catre internet
            var response = await client.GetAsync($"https://api.github.com/users/{username}/repos");
            
            if (!response.IsSuccessStatusCode)
            {
                return NotFound($"Eroare GitHub API. Probabil contul '{username}' nu exista.");
            }

            // Citim si trimitem JSON-ul pur mai departe aplicatiei noastre
            var content = await response.Content.ReadAsStringAsync();
            return Content(content, "application/json");
        }
    }
}
