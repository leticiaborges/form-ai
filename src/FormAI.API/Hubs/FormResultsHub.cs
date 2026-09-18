using System.Security.Claims;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FormAI.API.Hubs;

[Authorize]
public class FormResultsHub : Hub<IFormResultsClient>
{
    private readonly IFormRepository _forms;

    public FormResultsHub(IFormRepository forms)
    {
        _forms = forms;
    }

    public static string GroupName(Guid formId) => $"form-results:{formId}";

    public async Task JoinFormResults(Guid formId)
    {
        var userId = GetUserId();
        var form = await _forms.GetByIdAsync(formId);

        try
        {
            FormAccessValidator.CheckOwnerAccess(form, userId);
        }
        catch (NotFoundException)
        {
            throw new HubException("Form not found");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId,
            GroupName(formId));
    }

    private Guid GetUserId()
    {
        var idClaim = Guid.Parse((Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub"))!);

        return idClaim;
    }

}