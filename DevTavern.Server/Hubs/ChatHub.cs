using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace DevTavern.Server.Hubs
{
    // SignalR Hub - comunicare in timp real intre utilizatori
    public class ChatHub : Hub
    {
        // Trimite un mesaj live catre toti utilizatorii conectati
        public async Task SendLiveMessage(string username, string messageContent)
        {
            await Clients.All.SendAsync("ReceiveMessage", username, messageContent);
        }
    }
}
