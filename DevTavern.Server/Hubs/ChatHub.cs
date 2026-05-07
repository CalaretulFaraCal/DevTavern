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

        // Voice channel membership: channelKey -> set of usernames
        private static readonly ConcurrentDictionary<string, HashSet<string>> _voiceChannelMembers = new();
        // Tracks which voice channel each connection is in: ConnectionId -> (channelKey, username)
        private static readonly ConcurrentDictionary<string, (string channelKey, string username)> _connectionVoiceChannel = new();

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
                bool stillOnline = _userConnections.Values.Contains(username);
                if (!stillOnline)
                    await Clients.All.SendAsync("UserWentOffline", username);
            }

            if (_connectionVoiceChannel.TryRemove(Context.ConnectionId, out var voiceInfo))
            {
                if (_voiceChannelMembers.TryGetValue(voiceInfo.channelKey, out var members))
                {
                    lock (members) { members.Remove(voiceInfo.username); }
                }
                await Clients.Group($"VoiceChannel_{voiceInfo.channelKey}").SendAsync("UserLeftVoice", voiceInfo.username);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"VoiceChannel_{voiceInfo.channelKey}");
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

        // ================= Voice Chat (Walkie-Talkie) =================

        public async Task JoinVoiceChannel(string channelId, string username)
        {
            var members = _voiceChannelMembers.GetOrAdd(channelId, _ => new HashSet<string>());

            List<string> existing;
            lock (members)
            {
                existing = members.ToList();
                members.Add(username);
            }

            _connectionVoiceChannel[Context.ConnectionId] = (channelId, username);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"VoiceChannel_{channelId}");

            // Trimite celui care tocmai a intrat lista celor deja prezenti
            await Clients.Caller.SendAsync("VoiceChannelSnapshot", existing);

            // Anunta ceilalti din canal ca a intrat cineva nou
            await Clients.OthersInGroup($"VoiceChannel_{channelId}").SendAsync("UserJoinedVoice", username);
        }

        public async Task LeaveVoiceChannel(string channelId, string username)
        {
            if (_voiceChannelMembers.TryGetValue(channelId, out var members))
                lock (members) { members.Remove(username); }

            _connectionVoiceChannel.TryRemove(Context.ConnectionId, out _);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"VoiceChannel_{channelId}");
            await Clients.Group($"VoiceChannel_{channelId}").SendAsync("UserLeftVoice", username);
        }

        // Intercepteaza chunk-ul audio de la un client si il trimite celorlalti din acelasi canal
        public async Task SendAudioBuffer(string channelId, string username, byte[] audioData)
        {
            await Clients.GroupExcept($"VoiceChannel_{channelId}", Context.ConnectionId).SendAsync("ReceiveAudioBuffer", username, audioData);
        }
    }
}
