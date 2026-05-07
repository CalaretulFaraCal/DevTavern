using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DevTavern.Server.Hubs
{
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, string> _userConnections = new();

        // channelKey -> set of usernames
        private static readonly ConcurrentDictionary<string, HashSet<string>> _voiceChannelMembers = new();
        // ConnectionId -> (channelKey, username)
        private static readonly ConcurrentDictionary<string, (string channelKey, string username)> _connectionVoiceChannel = new();

        // channelKey = "{projectId}_{channelName}", deci projectId-ul se poate extrage
        private static string ProjectIdFromKey(string channelKey)
            => channelKey.Contains('_') ? channelKey[..channelKey.IndexOf('_')] : channelKey;

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

                var projectId = ProjectIdFromKey(voiceInfo.channelKey);
                await Clients.Group($"Project_{projectId}").SendAsync("UserLeftVoice", voiceInfo.channelKey, voiceInfo.username);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinChannel(string channelId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"Channel_{channelId}");

        public async Task LeaveChannel(string channelId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Channel_{channelId}");

        public async Task JoinProject(string projectId)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"Project_{projectId}");

        public async Task LeaveProject(string projectId)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Project_{projectId}");

        public async Task NotifyRolesChanged(string projectId, string username, string newRolesCsv)
            => await Clients.Group($"Project_{projectId}").SendAsync("RolesChanged", username, newRolesCsv);

        public async Task NotifyChannelCreated(string projectId, int channelId, string channelName)
            => await Clients.Group($"Project_{projectId}").SendAsync("ChannelCreated", channelId, channelName);

        public async Task NotifyChannelDeleted(string projectId, int channelId)
            => await Clients.Group($"Project_{projectId}").SendAsync("ChannelDeleted", channelId);

        public async Task SendLiveMessage(string channelId, string username, string avatarUrl, string messageContent)
            => await Clients.Group($"Channel_{channelId}").SendAsync("ReceiveMessage", username, avatarUrl, messageContent);

        // ================= Voice Chat =================

        public async Task JoinVoiceChannel(string channelKey, string username)
        {
            var members = _voiceChannelMembers.GetOrAdd(channelKey, _ => new HashSet<string>());

            List<string> existing;
            lock (members)
            {
                existing = members.ToList();
                members.Add(username);
            }

            _connectionVoiceChannel[Context.ConnectionId] = (channelKey, username);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"VoiceChannel_{channelKey}");

            var projectId = ProjectIdFromKey(channelKey);

            // Trimite celui care intra cate un UserJoinedVoice pentru fiecare user deja prezent
            foreach (var existingUser in existing)
                await Clients.Caller.SendAsync("UserJoinedVoice", channelKey, existingUser);

            // Anunta tot proiectul ca a intrat un nou user
            await Clients.Group($"Project_{projectId}").SendAsync("UserJoinedVoice", channelKey, username);
        }

        public async Task LeaveVoiceChannel(string channelKey, string username)
        {
            if (_voiceChannelMembers.TryGetValue(channelKey, out var members))
                lock (members) { members.Remove(username); }

            _connectionVoiceChannel.TryRemove(Context.ConnectionId, out _);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"VoiceChannel_{channelKey}");

            var projectId = ProjectIdFromKey(channelKey);
            await Clients.Group($"Project_{projectId}").SendAsync("UserLeftVoice", channelKey, username);
        }

        public async Task SendAudioBuffer(string channelKey, string username, byte[] audioData)
            => await Clients.GroupExcept($"VoiceChannel_{channelKey}", Context.ConnectionId).SendAsync("ReceiveAudioBuffer", username, audioData);

        // Trimite caller-ului cate un UserJoinedVoice pentru fiecare user deja conectat in canalele de voce ale proiectului
        public async Task RequestProjectVoiceSnapshot(string projectId)
        {
            foreach (var kvp in _voiceChannelMembers)
            {
                if (!kvp.Key.StartsWith(projectId + "_")) continue;
                List<string> snapshot;
                lock (kvp.Value) { snapshot = kvp.Value.ToList(); }
                foreach (var u in snapshot)
                    await Clients.Caller.SendAsync("UserJoinedVoice", kvp.Key, u);
            }
        }
    }
}
