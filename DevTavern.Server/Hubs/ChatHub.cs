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
        // Thread-safe dictionary for connection tracking (ConnectionId -> (ProjectId, Username))
        private static readonly ConcurrentDictionary<string, (string ProjectId, string Username)> _userConnections = new();

        // Alatura utilizatorul unui grup specific (Canalul selectat)
        public async Task JoinChannel(string channelId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
        }

        public async Task LeaveChannel(string channelId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Channel_{channelId}");
        }

        // ==========================================================
        // PROIECTE: Notificari, Prezenta Online si Roluri
        // ==========================================================

        public async Task<List<string>> JoinProject(string projectId, string username)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Project_{projectId}");
            _userConnections[Context.ConnectionId] = (projectId, username);

            // Anuntam restul proiectului ca acest utilizator a intrat (e online)
            await Clients.GroupExcept($"Project_{projectId}", Context.ConnectionId).SendAsync("UserJoinedProject", username);

            // Returnam lista cu numele utilizatorilor care sunt DEJA online in acest proiect
            var onlineUsers = _userConnections.Values
                .Where(v => v.ProjectId == projectId)
                .Select(v => v.Username)
                .Distinct()
                .ToList();

            return onlineUsers;
        }

        public async Task LeaveProject(string projectId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Project_{projectId}");
            
            if (_userConnections.TryRemove(Context.ConnectionId, out var data))
            {
                // Daca nu mai are alte conexiuni in acelasi proiect (poate are 2 taburi), dam offline
                bool stillOnline = _userConnections.Values.Any(v => v.ProjectId == projectId && v.Username == data.Username);
                if (!stillOnline)
                {
                    await Clients.Group($"Project_{projectId}").SendAsync("UserLeftProject", data.Username);
                }
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_userConnections.TryRemove(Context.ConnectionId, out var data))
            {
                bool stillOnline = _userConnections.Values.Any(v => v.ProjectId == data.ProjectId && v.Username == data.Username);
                if (!stillOnline)
                {
                    await Clients.Group($"Project_{data.ProjectId}").SendAsync("UserLeftProject", data.Username);
                }
            }
            await base.OnDisconnectedAsync(exception);
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
