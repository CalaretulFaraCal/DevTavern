using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace DevTavern.Server.Hubs
{
    // SignalR Hub - comunicare in timp real intre utilizatori
    public class ChatHub : Hub
    {
        // Alatura utilizatorul unui grup specific (Canalul selectat)
        public async Task JoinChannel(string channelId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
        }

        // Paraseste un grup anterior (pentru a nu ramane conectat la canale vechi)
        public async Task LeaveChannel(string channelId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
        }

        // Metode pentru Proiecte (pentru a auzi cand se creeaza canale noi)
        public async Task JoinProject(string projectId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Project_{projectId}");
        }

        public async Task LeaveProject(string projectId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Project_{projectId}");
        }

        public async Task NotifyChannelCreated(string projectId, int channelId, string channelName)
        {
            await Clients.Group($"Project_{projectId}").SendAsync("ChannelCreated", channelId, channelName);
        }

        public async Task NotifyChannelDeleted(string projectId, int channelId)
        {
            await Clients.Group($"Project_{projectId}").SendAsync("ChannelDeleted", channelId);
        }

        // Trimite un mesaj live doar catre utilizatorii care sunt in acelasi grup (Canal)
        public async Task SendLiveMessage(string channelId, string username, string avatarUrl, string messageContent)
        {
            await Clients.Group($"Channel_{channelId}").SendAsync("ReceiveMessage", username, avatarUrl, messageContent);
        }
    }
}
