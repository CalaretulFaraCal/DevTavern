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

        // Trimite un mesaj live doar catre utilizatorii care sunt in acelasi grup (Canal)
        public async Task SendLiveMessage(string channelId, string username, string messageContent)
        {
            await Clients.Group($"Channel_{channelId}").SendAsync("ReceiveMessage", username, messageContent);
        }
    }
}
