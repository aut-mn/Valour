using IdGen;
using Microsoft.AspNetCore.Mvc;
using Valour.Server.API;
using Valour.Server.Cdn;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Server.Redis;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class DirectChatChannelApi
{
        
    [ValourRoute(HttpVerbs.Get, "api/directchatchannels/{id}")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetRoute(
        long id, 
        DirectChatChannelService directService)
    {
        // id is the id of the channel
        var channel = await directService.GetAsync(id);

        return channel is null ? ValourResult.NotFound<DirectChatChannel>() : 
            Results.Json(channel);
    }

    [ValourRoute(HttpVerbs.Get, "api/directchatchannels/byuser/{id}")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetViaTargetRoute(
        long id,
        DirectChatChannelService directService,
        UserService userService)
    {
        // id is the id of the target user, not the channel!
        var requesterUserId = await userService.GetCurrentUserId();//ctx.GetToken();

        // Ensure target user exists
        var targetuser = await userService.GetAsync(id);
        if (targetuser is null)
            return ValourResult.NotFound("Target user not found");

        var channel = await directService.GetAsync(requesterUserId, id);
        
        if (channel is not null) return Results.Json(channel);

        var result = await directService.CreateAsync(requesterUserId, id);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    // Message routes

    [ValourRoute(HttpVerbs.Get, "api/directchatchannels/{id}/message/{messageId}")]
    [UserRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id, 
        long messageId,
        DirectChatChannelService directService,
        UserService userService)
    {
        var requesterUserId = await userService.GetCurrentUserId();

        var channel = await directService.GetAsync(id);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != requesterUserId) &&
            (channel.UserTwoId != requesterUserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");

        return Results.Json(await directService.GetDirectMessageAsync(messageId));
    }

    [ValourRoute(HttpVerbs.Get, "api/directchatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id,
        DirectChatChannelService directService,
        UserService userService,
        [FromQuery] long index = long.MaxValue, 
        [FromQuery] int count = 10)
    {
        if (count > 64)
            return Results.BadRequest("Maximum count is 64.");

        var requesterUserId = await userService.GetCurrentUserId();

        var channel = await directService.GetAsync(id);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != requesterUserId) &&
            (channel.UserTwoId != requesterUserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");


        var messages = await directService.GetDirectMessagesAsync(channel, index, count);

        await directService.UpdateUserStateAsync(channel, requesterUserId);

        return Results.Json(messages);
    }

    [ValourRoute(HttpVerbs.Post, "api/directchatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] DirectMessage message,
        DirectChatChannelService directService,
        UserService userService)
    {
        var requesterUserId = await userService.GetCurrentUserId();

        if (message is null)
            return Results.BadRequest("Include message in body.");

        if (string.IsNullOrEmpty(message.Content) &&
            string.IsNullOrEmpty(message.EmbedData) &&
            string.IsNullOrEmpty(message.AttachmentsData))
            return Results.BadRequest("Message content cannot be null");

        if (message.Fingerprint is null)
            return Results.BadRequest("Please include a Fingerprint.");

        if (message.AuthorUserId != requesterUserId)
            return Results.BadRequest("UserId must match sender.");

        if (message.Content != null && message.Content.Length > 2048)
            return Results.BadRequest("Content must be under 2048 chars");


        if (message.EmbedData != null && message.EmbedData.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");

        var channel = await directService.GetAsync(message.ChannelId);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != requesterUserId) &&
            (channel.UserTwoId != requesterUserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");

        var result = await directService.PostMessageAsync(channel, message, requesterUserId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Delete, "api/directchatchannels/{id}/messages/{message_id}")]
    [UserRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long id, 
        long message_id,
        DirectChatChannelService directService,
        UserService userService)
    {
        var requesterUserId = await userService.GetCurrentUserId();

        var channel = await directService.GetAsync(id);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != requesterUserId) &&
            (channel.UserTwoId != requesterUserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");

        var message = await directService.GetDirectMessageAsync(message_id);

        if (message.ChannelId != id)
            return ValourResult.NotFound<PlanetMessage>();

        if (requesterUserId != message.AuthorUserId)
            return ValourResult.Forbid("You cannot delete another user's direct messages");

        var result = await directService.DeleteMessageAsync(channel, message);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/directchatchannels/relay")]
    public static async Task<IResult> RelayDirectMessageAsync(
        [FromBody] DirectMessage message, 
        [FromQuery] string auth, 
        [FromQuery] long targetId,
        CoreHubService hubService)
    {
        if (auth != NodeConfig.Instance.Key)
            return ValourResult.Forbid("Invalid inter-node key.");

        hubService.RelayDirectMessage(message, targetId);

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Post, "api/directchatchannels/relaydelete")]
    public static async Task<IResult> RelayDeleteDirectMessageAsync(
        [FromBody] DirectMessage message, 
        [FromQuery] string auth, 
        [FromQuery] long targetId,
        CoreHubService hubService)
    {
        if (auth != NodeConfig.Instance.Key)
            return ValourResult.Forbid("Invalid inter-node key.");

        hubService.NotifyDirectMessageDeletion(message, targetId);

        return Results.Ok();
    }
}