using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace DevTavern.Server.Hubs
{
    // Hub-ul este sala principala de chat in timp real (pentru Cerinta 11)
    public class ChatHub : Hub
    {
        // Aceasta e functia la care Frontend-ul se va conecta prin teava "SignalR"
        // Cand colegul la Frontend apeleaza asta...
        public async Task SendLiveMessage(string username, string messageContent)
        {
            // ...Serverul "striga" peste tot internentul la absolut toti utilizatorii conectati, cu mesajul venit!
            await Clients.All.SendAsync("ReceiveMessage", username, messageContent);
        }
    }
}
