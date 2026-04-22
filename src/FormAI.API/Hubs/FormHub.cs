using Microsoft.AspNetCore.SignalR;

namespace FormAI.API.Hubs;

public class FormHub : Hub
{
    // ReceiveSubmission — broadcast to owner when a new response arrives
    // GenerationProgress — streams AI generation progress to the creator
}
