using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevTavern.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IRepository<User> _userRepository;

        public UsersController(IRepository<User> userRepository)
        {
            _userRepository = userRepository;
        }

        // GET /api/users
        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            var users = await _userRepository.GetAllAsync();
            return Ok(users);
        }

        // GET /api/users/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUserById(int id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            
            if (user == null)
            {
                return NotFound($"Utilizatorul cu ID {id} nu exista.");
            }

            return Ok(user);
        }

        // POST /api/users
        [HttpPost]
        public async Task<ActionResult<User>> CreateUser(User newUser)
        {
            if (newUser == null)
            {
                return BadRequest("Datele utilizatorului sunt invalide.");
            }

            await _userRepository.AddAsync(newUser);
            
            return CreatedAtAction(nameof(GetUserById), new { id = newUser.Id }, newUser);
        }
    }
}
