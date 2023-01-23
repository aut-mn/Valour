using Microsoft.AspNetCore.Mvc;
using Valour.Database;
using Valour.Server.Database;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Services;
using Valour.Shared;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetMemberApi
{
     // Helpful route to return the member for the authorizing user
    [ValourRoute(HttpVerbs.Get, "api/members/self/{planetId}")]
    public static async Task<IResult> GetSelfRouteAsync(
        long planetId, 
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotFound("Member not found");

        return Results.Json(member);
    }
        

    [ValourRoute(HttpVerbs.Get, "api/members/{id}")]
    public static async Task<IResult> GetRouteAsync(
        long id, 
        PlanetMemberService service)
    {
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(id);
        if (self is null)
            return ValourResult.NotPlanetMember();

        // Get other member
        var member = await service.GetAsync(id);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/authority")]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetAuthorityRouteAsync(long id, PlanetMemberService memberService)
    {
        var member = await memberService.GetAsync(id);
        if (member is null)
            return ValourResult.NotFound<Models.PlanetMember>();

        var authority = await memberService.GetAuthorityAsync(member);
        
        return Results.Json(authority);
    }

    [ValourRoute(HttpVerbs.Get, "/byuser/{userId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetRoute(
        long planetId, 
        long userId,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetByUserAsync(userId, planetId);
        if (member is null)
            return ValourResult.NotFound<Models.PlanetMember>();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] Models.PlanetMember member, 
        long planetId, 
        string inviteCode, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var token = ctx.GetToken();

        if (member.PlanetId != planetId)
            return Results.BadRequest("PlanetId does not match.");
        if (member.UserId != token.UserId)
            return Results.BadRequest("UserId does not match.");

        var nameValid = ValidateName(member);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        // Clear out pfp, it *must* be done through VMPS
        member.MemberPfp = null;

        // Ensure member does not already exist
        if (await db.PlanetMembers.AnyAsync(x => x.PlanetId == planetId && x.UserId == token.UserId))
            return Results.BadRequest("Planet member already exists.");

        var planet = await FindAsync<Planet>(planetId, db);

        if (!planet.Public)
        {
            if (inviteCode is null)
                return ValourResult.Forbid("The planet is not public. Please include inviteCode.");

            if (!await db.PlanetInvites.AnyAsync(x => x.Code == inviteCode && x.PlanetId == planetId && DateTime.UtcNow > x.TimeCreated))
                return ValourResult.Forbid("The invite code is invalid or expired.");
        }

        try
        {
            await db.AddAsync(member);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(member);

        return Results.Created(member.GetUri(), member);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetMember member, 
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetMember> logger)
    {
        var token = ctx.GetToken();

        var old = await FindAsync<PlanetMember>(id, db);

        if (old is null)
            return ValourResult.NotFound<PlanetMember>();

        if (old.Id != member.Id)
            return Results.BadRequest("Cannot change Id.");

        if (token.UserId != member.UserId)
            return Results.BadRequest("You can only modify your own membership.");

        if (member.PlanetId != planetId)
            return Results.BadRequest("Cannot change PlanetId.");

        if (old.MemberPfp != member.MemberPfp)
            return Results.BadRequest("Cannot directly change pfp. Use VMPS.");

        var nameValid = ValidateName(member);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        try
        {
            db.Entry(old).State = EntityState.Detached;
            db.PlanetMembers.Update(member);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }
        
        hubService.NotifyPlanetItemChange(member);

        return Results.Ok(member);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> DeleteRouteAsync(
        long id, 
        HttpContext ctx,
        CoreHubService hubService,
        PlanetMemberService memberService)
    {
        var authMember = ctx.GetMember();
        var targetMember = await memberService.GetAsync(id);

        // You can always delete your own membership, so we only check permissions
        // if you are not the same as the target
        if (authMember.Id != id)
        {
            if (!await memberService.HasPermissionAsync(authMember, PlanetPermissions.Kick))
                return ValourResult.LacksPermission(PlanetPermissions.Kick);

            if (await memberService.GetAuthorityAsync(authMember) < await memberService.GetAuthorityAsync(targetMember))
                return ValourResult.Forbid("You have less authority than the target member.");
        }

        await memberService.DeleteAsync(targetMember);
        hubService.NotifyPlanetItemDelete(targetMember);

        return Results.NoContent();
    }

    private static TaskResult ValidateName(PlanetMember member)
    {
        // Ensure nickname is valid
        return member.Nickname.Length > 32 ? new TaskResult(false, "Maximum nickname is 32 characters.") : 
            TaskResult.SuccessResult;
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roles"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetAllRolesForMember(long id, long planetId, ValourDB db)
    {
        var member = await db.PlanetMembers.Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                                           .ThenInclude(x => x.Role)
                                           .FirstOrDefaultAsync(x => x.Id == id && x.PlanetId == planetId);
        
        return member is null ? ValourResult.NotFound<PlanetMember>() : 
            Results.Json(member.RoleMembership.Select(r => r.RoleId));
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/roles/{roleId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> AddRoleToMember(
        long id, 
        long planetId, 
        long roleId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PlanetMemberService memberService,
        PermissionsService permService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.PlanetId != planetId)
            return ValourResult.NotFound<PlanetMember>();
        
        if (!await memberService.HasPermissionAsync(authMember, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await db.PlanetRoleMembers.AnyAsync(x => x.MemberId == member.Id && x.RoleId == roleId))
            return Results.BadRequest("The member already has this role");

        var role = await db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await memberService.GetAuthorityAsync(authMember);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have lower authority than the role you are trying to add");

        PlanetRoleMember newRoleMember = new()
        {
            Id = IdManager.Generate(),
            MemberId = member.Id,
            RoleId = roleId,
            UserId = member.UserId,
            PlanetId = member.PlanetId
        };

        try
        {
            await db.PlanetRoleMembers.AddAsync(newRoleMember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(newRoleMember);

        return Results.Created(newRoleMember.GetUri(), newRoleMember);
    }


    [ValourRoute(HttpVerbs.Delete, "/{id}/roles/{roleId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> RemoveRoleFromMember(
        long id, 
        long planetId, 
        long roleId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        PlanetMemberService memberService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.PlanetId != planetId)
            return ValourResult.NotFound<PlanetMember>();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, permService))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var oldRoleMember = await db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.MemberId == member.Id && x.RoleId == roleId);

        if (oldRoleMember is null)
            return Results.BadRequest("The member does not have this role");

        var role = await db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await authMember.GetAuthorityAsync(memberService);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have less authority than the role you are trying to remove"); ;

        try
        {
            db.PlanetRoleMembers.Remove(oldRoleMember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemDelete(oldRoleMember);

        return Results.NoContent();
    }
}