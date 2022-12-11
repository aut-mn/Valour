﻿using Microsoft.AspNetCore.SignalR;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Channels;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared;
using Valour.Server.Hubs;
using Valour.Server.Services;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database
{
    public class CoreHub : Hub
    {
        public const string HubUrl = "/corehub";

        private readonly ValourDB _db;
        private readonly CoreHubService _hubService;
        private readonly UserOnlineService _onlineService;
        private readonly ChannelStateService _stateService;

        public CoreHub(
            ValourDB db, 
            CoreHubService hubService, 
            UserOnlineService onlineService,
            ChannelStateService stateService)
        {
            _db = db;
            _hubService = hubService;
            _onlineService = onlineService;
            _stateService = stateService;
        }

        public async Task<TaskResult> Authorize(string token)
        {
            // Authenticate user
            AuthToken authToken = await AuthToken.TryAuthorize(token, _db);

            if (authToken is null)
                return new TaskResult(false, "Failed to authenticate connection.");

            ConnectionTracker.ConnectionIdentities[Context.ConnectionId] = authToken;

            return new TaskResult(true, "Authenticated with SignalR hub successfully.");
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await ConnectionTracker.RemovePrimaryConnection(Context, _db);
            ConnectionTracker.RemoveAllMemberships(Context);

            await base.OnDisconnectedAsync(exception);
        }
        
        /// <summary>
        /// Primary node connection for user-wide events
        /// </summary>
        public async Task<TaskResult> JoinUser()
        {
            var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
            if (authToken == null) return new TaskResult(false, "Failed to connect to User: SignalR was not authenticated.");

            var groupId = $"u-{authToken.UserId}";

            ConnectionTracker.TrackGroupMembership(groupId, Context);
            await ConnectionTracker.AddPrimaryConnection(authToken.UserId, Context, _db);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

            return new TaskResult(true, "Connected to user " + groupId);
        }
        
        public async Task LeaveUser()
        {
            var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
            if (authToken == null) return;

            var groupId = $"u-{authToken.UserId}";

            ConnectionTracker.UntrackGroupMembership(groupId, Context);
            await ConnectionTracker.RemovePrimaryConnection(Context, _db);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }

        public async Task<TaskResult> JoinPlanet(long planetId)
        {
            var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
            if (authToken == null) return new TaskResult(false, "Failed to connect to Planet: SignalR was not authenticated.");

            await _onlineService.UpdateOnlineState(authToken.UserId);
            
            PlanetMember member = await _db.PlanetMembers.FirstOrDefaultAsync(
                x => x.UserId == authToken.UserId && x.PlanetId == planetId);

            // If the user is not a member, cancel
            if (member == null)
            {
                return new TaskResult(false, "Failed to connect to Planet: You are not a member.");
            }
            
            var groupId = $"p-{planetId}";
            ConnectionTracker.TrackGroupMembership(groupId, Context);

            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);


            return new TaskResult(true, "Connected to planet " + planetId);
        }

        public async Task LeavePlanet(long planetId) {
            var groupId = $"p-{planetId}";
            ConnectionTracker.UntrackGroupMembership(groupId, Context);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }


        public async Task<TaskResult> JoinChannel(long channelId)
        {
            var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
            if (authToken == null) return new TaskResult(false, "Failed to connect to Channel: SignalR was not authenticated.");

            await _onlineService.UpdateOnlineState(authToken.UserId);
            
            // Grab channel
            var channel = await _db.PlanetChatChannels.FindAsync(channelId);
            if (channel is null)
                return new TaskResult(false, "Failed to connect to Channel: Channel was not found.");
            
            PlanetMember member = await _db.PlanetMembers.FirstOrDefaultAsync(
                x => x.UserId == authToken.UserId && x.PlanetId == channel.PlanetId);

            if (!await channel.HasPermissionAsync(member, ChatChannelPermissions.ViewMessages, _db))
                return new TaskResult(false, "Failed to connect to Channel: Member lacks view permissions.");

            var groupId = $"c-{channelId}";

            ConnectionTracker.TrackGroupMembership(groupId, Context);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
            
            var channelState = await _db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == authToken.UserId && x.ChannelId == channel.Id);

            if (channelState is null)
            {
                channelState = new UserChannelState()
                {
                    UserId = authToken.UserId,
                    ChannelId = channelId
                };

                _db.UserChannelStates.Add(channelState);
            }
            
            channelState.LastViewedState = await _stateService.GetState(channelId);
            await _db.SaveChangesAsync();
            
            _hubService.NotifyUserChannelStateUpdate(authToken.UserId, channelState);

            return new TaskResult(true, "Connected to channel " + channelId);
        }

        public async Task LeaveChannel(long channelId) {
            var groupId = $"c-{channelId}";
            ConnectionTracker.UntrackGroupMembership(groupId, Context);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }


        public async Task JoinInteractionGroup(long planetId)
        {
            var authToken = ConnectionTracker.GetToken(Context.ConnectionId);
            if (authToken == null) return;

            PlanetMember member = await _db.PlanetMembers.FirstOrDefaultAsync(
                x => x.UserId == authToken.UserId && x.PlanetId == planetId);

            // If the user is not a member, cancel
            if (member == null)
                return;
            
            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"i-{planetId}");
        }

        public async Task LeaveInteractionGroup(long planetId) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"i-{planetId}");
        
        public string Ping() => "Pong";
    }
}