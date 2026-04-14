using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client.Services
{
    public class GitHubAuthService
    {
        private const string ClientId = "Ov23liOMdipPuxEsQX05";
        private const string RedirectUri = "http://localhost:12345/callback/";
        private const string TokenFilePath = "github_token.cache";

        public async Task<string> LoginAndGetTokenAsync()
        {
            // Verificăm dacă avem deja tokenul salvat din sesiunea trecută (Cerere: "sa nu te loghezi mereu")
            if (File.Exists(TokenFilePath))
            {
                string savedToken = await File.ReadAllTextAsync(TokenFilePath);
                if (!string.IsNullOrWhiteSpace(savedToken))
                    return savedToken; // Sarim complet peste browser!
            }

            using var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();

            string authUrl = $"https://github.com/login/oauth/authorize?client_id={ClientId}&redirect_uri={RedirectUri}&scope=repo user:email&prompt=consent&login=";
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            var context = await listener.GetContextAsync();
            string? code = context.Request.QueryString["code"];

            string responseString = "<html><body><h1>Authentication successful!</h1><p>You can close this window and return to the application.</p></body></html>";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            context.Response.ContentLength64 = buffer.Length;
            var responseOutput = context.Response.OutputStream;
            await responseOutput.WriteAsync(buffer, 0, buffer.Length);
            responseOutput.Close();

            listener.Stop();

            if (string.IsNullOrEmpty(code))
                throw new Exception("No authorization code received from GitHub.");

            return await ExchangeCodeAtOurBackendAsync(code);
        }

        private async Task<string> ExchangeCodeAtOurBackendAsync(string code)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var payload = new
            {
                Code = code,
                RedirectUri = RedirectUri
            };

            var content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://devtavern.onrender.com/api/auth/github-token", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errDetail = await response.Content.ReadAsStringAsync();
                throw new Exception($"Backend Error: {errDetail}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(responseBody);
            
            string? accessToken = json["access_token"]?.ToString();
            
            // Când login-ul a mers perfect, memorăm token-ul local.
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                await File.WriteAllTextAsync(TokenFilePath, accessToken);
            }

            return accessToken ?? string.Empty;
        }

        /// <summary>
        /// Sterge token-ul salvat local — forteaza re-autentificare la urmatorul login.
        /// </summary>
        public static void ClearTokenCache()
        {
            if (File.Exists(TokenFilePath))
            {
                File.Delete(TokenFilePath);
            }
        }
    }
}
