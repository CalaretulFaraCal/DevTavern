# DevTavern

DevTavern is a desktop collaboration platform for developers. It combines real-time text chat, voice channels, and a built-in code browser, all connected to GitHub.

## What the project does

DevTavern allows developer teams to communicate and collaborate around their GitHub projects without switching between multiple tools. The main features are:

- Real-time text messaging with image attachments and code snippet sharing
- Voice channels with Voice Activity Detection (VAD) and Push-To-Talk
- A built-in code browser with syntax highlighting for GitHub repositories
- Automatic project and channel creation when importing GitHub repos
- A live activity feed showing recent commits, issues, and pull requests
- GitHub OAuth login with encrypted token caching

## Why the project is useful

Developers typically use separate tools for communication (Discord, Slack), version control (GitHub), and code browsing (IDE). Switching between them breaks focus and slows down collaboration. DevTavern brings these workflows together in a single application where discussions happen alongside the code they reference.

## How to get started

### Prerequisites

- .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
- Docker Desktop (https://www.docker.com/products/docker-desktop/) for the database
- A GitHub OAuth App (https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/creating-an-oauth-app)
- Windows 10 or 11

### Setup

1. Clone the repository:

       git clone https://github.com/CalaretulFaraCal/DevTavern.git
       cd DevTavern

2. Create a `.env` file in the root directory:

       DB_PASSWORD=YourDatabasePassword
       GITHUB_CLIENT_ID=your_github_oauth_client_id
       GITHUB_CLIENT_SECRET=your_github_oauth_client_secret

3. Update the connection string password in `DevTavern.Server/appsettings.json`.

4. Start the server:

       docker-compose up -d

   Or run directly:

       cd DevTavern.Server
       dotnet run

5. Run the client:

       cd DevTavern.Client
       dotnet run

### Running the pre-built executable

A pre-built Windows executable is available in the Releases section of this repository. Download `DevTavern.Client.exe` and run it directly. The executable connects to the hosted server and requires no additional setup beyond a GitHub account.

### Running tests

    cd DevTavern.Tests
    dotnet test

## Project structure

    DevTavern/
    ├── DevTavern.Client/          # WPF Desktop Application
    │   ├── Assets/                # Icons and sounds
    │   ├── Services/              # GitHub Auth Service
    │   ├── MainWindow.xaml/.cs    # Main UI and logic
    │   └── LoginWindow.xaml/.cs   # Login screen
    │
    ├── DevTavern.Server/          # ASP.NET Core Backend
    │   ├── Controllers/           # REST API endpoints
    │   ├── Hubs/                  # SignalR hubs (chat and voice)
    │   ├── Models/                # Database entity models
    │   ├── Repositories/         # Data access layer
    │   └── Factories/             # Channel generation
    │
    ├── DevTavern.Tests/           # Unit tests (xUnit)
    ├── docker-compose.yml
    └── .github/workflows/ci.yml   # CI/CD pipeline

## Where to get help

- Open an issue on this repository for bug reports or questions
- GitHub API documentation: https://docs.github.com/en/rest
- SignalR documentation: https://learn.microsoft.com/en-us/aspnet/core/signalr/

## Who maintains this project

Stoian Vladut-Nicolae — developer and project lead.

This project was developed as part of the IPDP university course.