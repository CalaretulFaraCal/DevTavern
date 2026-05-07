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
        // channelKey -> projectId (for broadcasting to project group)
        private static readonly ConcurrentDictionary<string, string> _voiceChannelProject = new();
        // ConnectionId -> (channelKey, projectId, username)
        private static readonly ConcurrentDictionary<string, (string channelKey, string projectId, string username)> _connectionVoiceChannel = new();

        public async Task<List<string>> GoOnline(string username)
        {
            _userConnections[Context.ConnectionId] = username;
            await Clients.All.SendAsync("UserWentOnline", username);
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
                    lock (members) { members.Remove(voiceInfo.username); }

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"VoiceChannel_{voiceInfo.channelKey}");
                await Clients.Group($"Project_{voiceInfo.projectId}").SendAsync("UserLeftVoice", voiceInfo.channelKey, voiceInfo.username);
            }

            await base.OnDisconnectedAsync(exception);
        }

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

        public async Task SendLiveMessage(string channelId, string username, string avatarUrl, string messageContent)
        {
            await Clients.Group($"Channel_{channelId}").SendAsync("ReceiveMessage", username, avatarUrl, messageContent);
        }

        // ================= Voice Chat =================

        public async Task JoinVoiceChannel(string channelKey, string projectId, string username)
        {
            var members = _voiceChannelMembers.GetOrAdd(channelKey, _ => new HashSet<string>());
            _voiceChannelProject[channelKey] = projectId;

            List<string> existing;
            lock (members)
            {
                existing = members.ToList();
                members.Add(username);
            }

            _connectionVoiceChannel[Context.ConnectionId] = (channelKey, projectId, username);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"VoiceChannel_{channelKey}");

            // Trimite celui care tocmai a intrat lista celor deja prezenti
            await Clients.Caller.SendAsync("VoiceChannelSnapshot", existing);

            // Anunta TOT proiectul ca a intrat cineva in canalul de voce
            await Clients.Group($"Project_{projectId}").SendAsync("UserJoinedVoice", channelKey, username);
        }

        public async Task LeaveVoiceChannel(string channelKey, string username)
        {
            if (_voiceChannelMembers.TryGetValue(channelKey, out var members))
                lock (members) { members.Remove(username); }

            _connectionVoiceChannel.TryRemove(Context.ConnectionId, out _);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"VoiceChannel_{channelKey}");

            if (_voiceChannelProject.TryGetValue(channelKey, out var projectId))
                await Clients.Group($"Project_{projectId}").SendAsync("UserLeftVoice", channelKey, username);
        }

        public async Task SendAudioBuffer(string channelKey, string username, byte[] audioData)
        {
            await Clients.GroupExcept($"VoiceChannel_{channelKey}", Context.ConnectionId).SendAsync("ReceiveAudioBuffer", username, audioData);
        }

        // Returneaza starea voice pentru toate canalele unui proiect
        public Task<Dictionary<string, List<string>>> GetProjectVoiceState(string projectId)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var kvp in _voiceChannelProject)
            {
                if (kvp.Value != projectId) continue;
                if (_voiceChannelMembers.TryGetValue(kvp.Key, out var members))
                {
                    List<string> snapshot;
                    lock (members) { snapshot = members.ToList(); }
                    if (snapshot.Count > 0)
                        result[kvp.Key] = snapshot;
                }
            }
            return Task.FromResult(result);
        }
    }
}
