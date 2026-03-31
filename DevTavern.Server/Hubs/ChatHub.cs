using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace DevTavern.Server.Hubs
{
    // SignalR Hub - comunicare in timp real intre utilizatori
    public class ChatHub : Hub
    {
        // Trimite un mesaj live catre toti utilizatorii conectati
        public async Task SendLiveMessage(string senderInstanceId, string username, string messageContent, int channelId, string avatarUrl)
        {
            await Clients.All.SendAsync("ReceiveMessage", senderInstanceId, username, messageContent, channelId, avatarUrl);
        }
    }
}
