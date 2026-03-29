using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetEnv;
using Newtonsoft.Json.Linq;

namespace DevTavern.Client.Services
{
    public class GitHubAuthService
    {
        // Variabile statice goale pe care le vom umple din .env
        private readonly string ClientId;
        private readonly string ClientSecret;
        
        // Portul pe care ascultăm în WPF
        private const string RedirectUri = "http://localhost:12345/callback/";

        public GitHubAuthService()
        {
            // Incarca secretele din fisierul .env cand serviciul e creat!
            Env.Load();
            ClientId = Env.GetString("GITHUB_CLIENT_ID");
            ClientSecret = Env.GetString("GITHUB_CLIENT_SECRET");
        }

        // Metoda principală
        public async Task<string> LoginAndGetTokenAsync()
        {
            // 1. Pregătim un server local micuț (invizibil) care să aștepte răspunsul de la GitHub
            using var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();

            // 2. Deschidem browser-ul default al user-ului direct pe pagina de permisiuni GitHub
            // scope=repo cere acces automat la toate proiectele!
            string authUrl = $"https://github.com/login/oauth/authorize?client_id={ClientId}&redirect_uri={RedirectUri}&scope=repo user:email";
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            // 3. Așteptăm ca GitHub să ne răspundă pe serverul nostru local (așteaptă până user-ul dă 'Authorize' în browser)
            var context = await listener.GetContextAsync();
            
            // Extragem codul din link-ul pe care l-a trimis GitHub
            string code = context.Request.QueryString["code"];

            // 4. Afișăm un mesaj frumos în browser și îl rugăm să-l închidă
            string responseString = "<html><body><h1>Autentificare cu succes!</h1><p>Poti inchide aceasta fereastra si sa te intorci in aplicatie.</p></body></html>";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            context.Response.ContentLength64 = buffer.Length;
            var responseOutput = context.Response.OutputStream;
            await responseOutput.WriteAsync(buffer, 0, buffer.Length);
            responseOutput.Close();

            // Oprim serverul local
            listener.Stop();

            if (string.IsNullOrEmpty(code))
                throw new Exception("Nu am primit cod de autorizare de la GitHub.");

            // 5. Acum că avem 'codul', trebuie să-l schimbăm pe un 'Access Token' oficial
            return await ExchangeCodeForTokenAsync(code);
        }

        private async Task<string> ExchangeCodeForTokenAsync(string code)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("client_secret", ClientSecret),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri)
            });

            var response = await client.PostAsync("https://github.com/login/oauth/access_token", content);
            var responseString = await response.Content.ReadAsStringAsync();

            var json = JObject.Parse(responseString);
            var accessToken = json["access_token"]?.ToString();

            return accessToken; // Acesta e tokenul pe care il va folosi aplicatia de acum incolo!
        }
    }
}
