# DevTavern

**DevTavern** is an integrated collaboration platform for developers, combining real-time chat, VoIP voice channels, and a built-in code browser — all connected natively to GitHub.

## Features

### Real-Time Chat
- Instant messaging powered by **SignalR** WebSockets
- Per-project text channels (auto-generated on import)
- Image attachments with client-side compression (Base64)
- Code snippet sharing with syntax highlighting
- Mention notifications with unread badges
- Message history persisted in PostgreSQL

### Voice Channels (VoIP)
- Real-time voice communication via SignalR audio streaming
- **Voice Activity Detection (VAD)** — mic activates only when you speak
- **Push-To-Talk (PTT)** — configurable hotkey support
- Per-user mute/deafen controls
- Input/output volume sliders and device selection

### Code Browser
- Browse any GitHub repository's file tree directly in the app
- Syntax highlighting powered by **AvalonEdit**
- Select lines and share them as code snippets in chat

### Home Dashboard
- Live GitHub activity feed (commits, issues, PRs) fetched from the GitHub Events API
- Project cards with quick navigation
- System status overview

### GitHub Authentication
- OAuth 2.0 login via GitHub
- Automatic avatar, display name, and repository import
- Token-based API authorization


##  Tech Stack

| Layer | Technology |
|:------|:-----------|
| **Client** | WPF (.NET 8), XAML, C# |
| **Server** | ASP.NET Core 8 Web API |
| **Real-Time** | SignalR (WebSockets) |
| **Database** | PostgreSQL (Neon) via EF Core |
| **Audio** | NAudio (capture/playback) |
| **Code Editor** | AvalonEdit |
| **Auth** | GitHub OAuth 2.0 |
| **Containerization** | Docker & Docker Compose |
| **Testing** | xUnit, Moq |
| **CI/CD** | GitHub Actions |



## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the server database)
- A [GitHub OAuth App](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/creating-an-oauth-app) (for authentication)
- Windows 10/11 (WPF client requirement)

## Getting Started

### 1. Clone the repository
```bash
git clone https://github.com/CalaretulFaraCal/DevTavern.git
cd DevTavern
```

### 2. Configure environment variables
Create a `.env` file in the root directory:
```env
DB_PASSWORD=YourDatabasePassword
GITHUB_CLIENT_ID=your_github_oauth_client_id
GITHUB_CLIENT_SECRET=your_github_oauth_client_secret
```

### 3. Update `appsettings.json`
Edit `DevTavern.Server/appsettings.json` and set your database connection string password.

### 4. Start the server
```bash
# Option A: Using Docker
docker-compose up -d

# Option B: Direct run
cd DevTavern.Server
dotnet run
```

### 5. Run the client
```bash
cd DevTavern.Client
dotnet run
```

## Project Structure

```
DevTavern/
├── DevTavern.Client/          # WPF Desktop Application
│   ├── Assets/                # Icons, sounds (.wav, .ico)
│   ├── Services/              # GitHub Auth Service
│   ├── MainWindow.xaml        # Main UI (XAML)
│   ├── MainWindow.xaml.cs     # Application logic
│   ├── LoginWindow.xaml       # Login screen
│   └── App.xaml               # App-level resources & themes
│
├── DevTavern.Server/          # ASP.NET Core Backend
│   ├── Controllers/           # REST API endpoints
│   │   ├── AuthController     # GitHub OAuth flow
│   │   ├── ProjectsController # Project CRUD
│   │   ├── ChannelsController # Channel management
│   │   ├── MessagesController # Chat messages
│   │   └── UsersController    # User profiles
│   ├── Hubs/                  # SignalR real-time hubs
│   ├── Models/                # EF Core entity models
│   ├── Repositories/          # Data access layer
│   ├── Factories/             # Channel factory pattern
│   ├── Migrations/            # EF Core DB migrations
│   └── Program.cs             # Server entry point
│
├── DevTavern.Tests/           # Unit Tests (xUnit)
│   ├── ChannelsControllerTests.cs
│   ├── MessagesControllerTests.cs
│   ├── ProjectsControllerTests.cs
│   ├── UsersControllerTests.cs
│   ├── ChannelFactoryTests.cs
│   └── ModelTests.cs
│
├── docker-compose.yml         # Docker configuration
├── .github/workflows/ci.yml  # CI/CD pipeline
└── README.md
```

## 📄 License

This project was developed as part of the **IPDP** university course.