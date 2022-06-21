﻿using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items;

/// <summary>
/// Enum for all Valour item types
/// </summary>
public enum ItemType
{
    Unknown,

    User,

    Message,

    Planet,
    Channel,
    PlanetChannel,
    PlanetChatChannel,
    PlanetCategoryChannel,

    PlanetMember,

    PlanetRole,
    PlanetRoleMember,

    PermissionsNode,

    PlanetMessage,

    PlanetInvite,
    OauthApp,
    AuthToken,
    PlanetBan,
    Referral,
    NotificationSubscription,
}

