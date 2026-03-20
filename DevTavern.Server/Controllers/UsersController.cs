using Microsoft.AspNetCore.Mvc;
using DevTavern.Server.Models;
using DevTavern.Server.Repositories;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevTavern.Server.Controllers
{
    // Calea de acces in browser va fi: https://localhost:5114/api/users
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        // Aceasta este Interfata generica pe care am facut-o anterior
        private readonly IRepository<User> _userRepository;

        // Dependency Injection (Principiul SOLID - D)
        // Nu cream baza de date manual, ci cerem sistemului sa ne dea "ceva" (o cutie) care se ocupa cu Useri
        public UsersController(IRepository<User> userRepository)
        {
            _userRepository = userRepository;
        }

        // Endpoint GET /api/users
        // Aducem toti utilizatorii (doar pt teste, nu vei baga asta in productie sa scoti 1 milion de oameni deodata)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            var users = await _userRepository.GetAllAsync();
            return Ok(users);
        }

        // Endpoint GET /api/users/1
        // Aduce un utilizator dupa ID
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

        // Endpoint POST /api/users
        // Metoda prin care se creeaza un utilizator nou
        [HttpPost]
        public async Task<ActionResult<User>> CreateUser(User newUser)
        {
            // Verificam sa nu faca spam un user trimitand "nimic"
            if (newUser == null)
            {
                return BadRequest("Datele utilizatorului sunt invalide.");
            }

            // Mai tarziu aici vom verifica daca exista deja contul de GitHub

            // Folosim functia automata din sablonul nostru
            await _userRepository.AddAsync(newUser);
            
            // Returnam rezultatul pe ecran cu Status 201 Created si unde il putem gasi
            return CreatedAtAction(nameof(GetUserById), new { id = newUser.Id }, newUser);
        }
    }
}
