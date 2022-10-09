﻿using Valour.Api.Client;
using Valour.Api.Items.Channels.Users;
using Valour.Api.Nodes;
using Valour.Shared;
using Valour.Shared.Items.Messages;

namespace Valour.Api.Items.Messages;

public class DirectMessage : Message, ISharedDirectMessage
{
    public static async ValueTask<DirectMessage> FindAsync(long id, long channelId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<DirectMessage>(id);
            if (cached is not null)
                return cached;
        }
        var response = await ValourClient.PrimaryNode.GetJsonAsync<DirectMessage>($"api/{nameof(DirectChatChannel)}/{channelId}/message/{id}");
        var item = response.Data;

        if (item is not null)
        {
            await ValourCache.Put(id, item);
        }

        return item;
    }

    public override Task<TaskResult> DeleteAsync() =>
        Node.DeleteAsync($"api/{nameof(DirectChatChannel)}/{ChannelId}/messages/{Id}");

    public override async ValueTask<string> GetAuthorColorAsync()
    {
        var user = await GetAuthorUserAsync();

        if (ValourClient.FriendFastLookup.Contains(user.Id))
            return "#9ffff1";

        return "#ffffff";
    }
    public override async ValueTask<string> GetAuthorImageUrlAsync() =>
        (await GetAuthorUserAsync()).PfpUrl;

    public override async ValueTask<string> GetAuthorNameAsync() =>
        (await GetAuthorUserAsync()).Name;

    public override async ValueTask<string> GetAuthorTagAsync()
    {
        var user = await GetAuthorUserAsync();

        if (user.Id == ValourClient.Self.Id)
            return "You";

        if (user.Bot)
            return "Bot";

        if (ValourClient.FriendFastLookup.Contains(user.Id))
            return "Friend";

        return "User";
    }
    public override async ValueTask<Message> GetReplyMessageAsync()
    {
        if (ReplyToId is null)
            return null;

        return await FindAsync(ReplyToId.Value, ChannelId);
    }

    public override Task<TaskResult> PostMessageAsync() =>
        ValourClient.PrimaryNode.PostAsync($"api/{nameof(DirectChatChannel)}/{ChannelId}/messages", this);
}
