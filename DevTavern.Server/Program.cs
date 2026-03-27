using DevTavern.Server.Data;
using DevTavern.Server.Factories;
using DevTavern.Server.Repositories;
using DevTavern.Server.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using AspNet.Security.OAuth.GitHub;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configureaza "Fabrica" invizibila care va da navale pe internet cand avem nevoie (Pentru extragere repo GitHub)
builder.Services.AddHttpClient();

// === Adăugiri obligatorii pentru frontend (CORS și WebSockets) === //
builder.Services.AddSignalR(); 

builder.Services.AddCors(options =>
{
    // Această politică permite oricărui "Calculator Străin / Front-End" să intre și să ne folosească porturile
    options.AddPolicy("AllowFrontendPolicy", policy => 
        policy.SetIsOriginAllowed(origin => true) // Permite React, Vue, Svelte, orice localhost:port
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});
// ================================================================ //

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddSingleton<IChannelFactory, ChannelFactory>();

// Configureaza Baza OAuth pentru GitHub
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/api/users/login";
})
.AddGitHub(options =>
{
    options.ClientId = builder.Configuration["Authentication:GitHub:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"]!;
    options.CallbackPath = "/signin-github";
    
    // Obtinem si adresa de email (necesara pt prof de multe ori)
    options.Scope.Add("user:email");
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// AUTO-MIGRATE DATABASE
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Aici e ușa oficială la care se va conecta aplicația React/Vite a colegului cu .withUrl('/chat')!
app.MapHub<ChatHub>("/chat");

app.Run();
