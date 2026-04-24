using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevTavern.Server.Hubs
{
    // SignalR Hub - comunicare in timp real intre utilizatori
    public class ChatHub : Hub
    {
        // Thread-safe dictionary for global connection tracking (ConnectionId -> Username)
        private static readonly ConcurrentDictionary<string, string> _userConnections = new();

        public async Task<List<string>> GoOnline(string username)
        {
            _userConnections[Context.ConnectionId] = username;
            
            // Anuntam toti utilizatorii conectati la server ca a intrat cineva
            await Clients.All.SendAsync("UserWentOnline", username);

            // Returnam lista globala cu toti utilizatorii conectati in acest moment
            return _userConnections.Values.Distinct().ToList();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_userConnections.TryRemove(Context.ConnectionId, out var username))
            {
                // Daca acest user nu mai are niciun tab/conexiune deschisa, il anuntam offline
                bool stillOnline = _userConnections.Values.Contains(username);
                if (!stillOnline)
                {
                    await Clients.All.SendAsync("UserWentOffline", username);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        // Alatura utilizatorul unui grup specific (Canalul selectat)
        public async Task JoinChannel(string channelId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
        }

        public async Task LeaveChannel(string channelId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
        }

        public async Task JoinProject(string projectId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Project_{projectId}");
        }

        public async Task LeaveProject(string projectId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Project_{projectId}");
        }

        public async Task NotifyRolesChanged(string projectId, string username, string newRolesCsv)
        {
            await Clients.Group($"Project_{projectId}").SendAsync("RolesChanged", username, newRolesCsv);
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
