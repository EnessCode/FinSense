using FinSense.Application.Features.Users.Commands.UpdateUserProfile;
using FinSense.Application.Features.Users.Contracts;
using FinSense.Application.Features.Users.Queries.GetCurrentUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
    {
        _sender = sender;
    }

    public record UpdateUserProfileRequest(string DisplayName, string PreferredLocale);

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileDto>> GetMe(CancellationToken cancellationToken)
    {
        var profile = await _sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return Ok(profile);
    }

    [HttpPatch("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileDto>> UpdateMe(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var updatedProfile = await _sender.Send(
            new UpdateUserProfileCommand(request.DisplayName, request.PreferredLocale),
            cancellationToken);

        return Ok(updatedProfile);
    }
}
