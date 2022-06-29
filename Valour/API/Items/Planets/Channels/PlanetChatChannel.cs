﻿using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;


/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetChatChannel : SyncedItem<PlanetChatChannel>, ISharedPlanetChatChannel, IPlanetChannel
{
    /// <summary>
    /// The Id of the planet this channel belongs to
    /// </summary>
    public ulong PlanetId { get; set; }

    /// <summary>
    /// The total number of messages sent in this channel
    /// </summary>
    public ulong MessageCount { get; set; }

    /// <summary>
    /// The name of this channel
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The position of this channel (lower is higher)
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// The description of this channel
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The Id of the parent category this channel belongs to
    /// </summary>
    public ulong? ParentId { get; set; }

    /// <summary>
    /// True if this channel inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    /// <summary>
    /// Returns the name of the item type
    /// </summary>
    public string GetHumanReadableName() => "Chat Channel";

    /// <summary>
    /// Returns the planet this channel belongs to
    /// </summary>
    public async Task<Planet> GetPlanetAsync(bool refresh = false) => 
        await Planet.FindAsync(PlanetId, refresh);

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool refresh = false) =>
        await GetChannelPermissionsNodeAsync(roleId, refresh);

    /// <summary>
    /// Returns the channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(ulong roleId, bool refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTarget.PlanetChatChannel, refresh);

    /// <summary>
    /// Returns the current total permissions for this channel for a member.
    /// This result is NOT SYNCED, since it flattens several nodes into one!
    /// </summary>
    public async Task<PermissionsNode> GetMemberPermissionsAsync(ulong memberId, bool force_refresh = false)
    {
        var member = await PlanetMember.FindAsync(memberId);
        var roles = await member.GetRolesAsync();

        // Start with no permissions
        var dummy_node = new PermissionsNode()
        {
            // Full, since values should either be yes or no
            Mask = Permission.FULL_CONTROL,
            // Default to no permission
            Code = 0x0,

            PlanetId = PlanetId,
            TargetId = Id,
            TargetType = PermissionsTarget.PlanetChatChannel
        };

        var planet = await GetPlanetAsync();

        // Easy cheat for owner
        if (planet.OwnerId == member.UserId)
        {
            dummy_node.Code = Permission.FULL_CONTROL;
            return dummy_node;
        }

        // Should be in order of most power -> least,
        // so we reverse it here
        for (int i = roles.Count - 1; i >= 0; i--)
        {
            var role = roles[i];
            var node = await GetChannelPermissionsNodeAsync(role.Id, force_refresh);

            foreach (var perm in ChatChannelPermissions.Permissions)
            {
                var val = node.GetPermissionState(perm);

                // Change nothing if undefined. Otherwise overwrite.
                // Since most important nodes come last, we will end with correct perms.
                if (val == PermissionState.True)
                {
                    dummy_node.SetPermission(perm, PermissionState.True);
                }
                else if (val == PermissionState.False)
                {
                    dummy_node.SetPermission(perm, PermissionState.False);
                }
            }
        }

        return dummy_node;
    } 

    public async Task<bool> HasPermissionAsync(ulong memberId, ChatChannelPermission perm) =>
        await ValourClient.GetJsonAsync<bool>($"{IdRoute}/checkperm/{memberId}/{perm.Value}");

    /// <summary>
    /// Returns the last (count) messages starting at (index)
    /// </summary>
    public async Task<List<PlanetMessage>> GetMessagesAsync(ulong index = ulong.MaxValue, int count = 10) =>
        await ValourClient.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?index={index}&count={count}");

    /// <summary>
    /// Returns the last (count) messages
    /// </summary>
    public async Task<List<PlanetMessage>> GetLastMessagesAsync(int count = 10) =>
        await ValourClient.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?count={count}");
}

