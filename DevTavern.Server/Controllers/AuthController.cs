using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DevTavern.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AuthController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public class GitHubExchangeRequest
        {
            public required string Code { get; set; }
            public required string RedirectUri { get; set; }
        }

        [HttpPost("github-token")]
        public async Task<IActionResult> ExchangeGitHubToken([FromBody] GitHubExchangeRequest request)
        {
            if (string.IsNullOrEmpty(request.Code))
            {
                return BadRequest("Code and RedirectUri are required.");
            }

            var clientId = _configuration["Authentication:GitHub:ClientId"];
            var clientSecret = _configuration["Authentication:GitHub:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                return StatusCode(500, "GitHub credentials are not configured on the server.");
            }

            var _httpClient = _httpClientFactory.CreateClient();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("code", request.Code),
                new KeyValuePair<string, string>("redirect_uri", request.RedirectUri)
            });

            var response = await _httpClient.PostAsync("https://github.com/login/oauth/access_token", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return BadRequest($"GitHub Error: {errorBody}");
            }

            var responseString = await response.Content.ReadAsStringAsync();
            using var jsonDocument = System.Text.Json.JsonDocument.Parse(responseString);
            var root = jsonDocument.RootElement;
            
            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorDesc = root.TryGetProperty("error_description", out var desc) 
                    ? desc.GetString() 
                    : errorElement.GetString();
                return BadRequest($"GitHub returned error: {errorDesc}");
            }

            var accessToken = root.TryGetProperty("access_token", out var tokenElement) 
                ? tokenElement.GetString() ?? string.Empty 
                : string.Empty;

            return Ok(new { access_token = accessToken });
        }
    }
}
