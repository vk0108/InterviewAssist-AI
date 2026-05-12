using System.Collections.Generic;
using System.Net.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();

var config = EnvironmentConfig.Build();

InterviewWorkflow.MapUploadEndpoint(app, config);
InterviewWorkflow.MapResumeUploadEndpoint(app, config);

// Returns a short-lived Azure Speech token so the frontend never sees the raw key
app.MapGet("/api/speech/token", async () =>
{
    var speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
    var speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");
    if (string.IsNullOrWhiteSpace(speechKey) || string.IsNullOrWhiteSpace(speechRegion))
        return Results.BadRequest(new { error = "Speech credentials not configured." });

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", speechKey);
    var response = await http.PostAsync(
        $"https://{speechRegion}.api.cognitive.microsoft.com/sts/v1.0/issueToken",
        null);
    if (!response.IsSuccessStatusCode)
        return Results.StatusCode((int)response.StatusCode);

    var token = await response.Content.ReadAsStringAsync();
    return Results.Ok(new { token, region = speechRegion });
});

app.MapPost("/api/screening/run", async (WorkflowRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.JobDescriptionJson))
    {
        return Results.BadRequest(new { error = "Job description is required." });
    }

    if (string.IsNullOrWhiteSpace(request.ResumeJson))
    {
        return Results.BadRequest(new { error = "Resume is required." });
    }

    var agents = AgentFactory.Create(config.ChatClient);
    var workflow = new InterviewWorkflow(config, agents);
    var result = await workflow.RunScreeningAsync(
        request.JobDescriptionJson,
        request.ResumeJson,
        request.Criteria);

    return Results.Ok(new
    {
        message = "Screening completed.",
        screeningText = result.ScreeningText,
        verdictLine = result.VerdictLine,
        passesCriteria = result.PassesCriteria,
        criteria = request.Criteria,
    });
}).DisableAntiforgery();

app.MapPost("/api/interview/run", async (WorkflowRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.JobDescriptionJson))
    {
        return Results.BadRequest(new { error = "Job description is required." });
    }

    if (string.IsNullOrWhiteSpace(request.ResumeJson))
    {
        return Results.BadRequest(new { error = "Resume is required." });
    }

    var agents = AgentFactory.Create(config.ChatClient);
    var workflow = new InterviewWorkflow(config, agents);
    var interview = await workflow.StartInterviewAsync(request.JobDescriptionJson, request.ResumeJson);

    return Results.Ok(new
    {
        message = "Interview started.",
        firstQuestion = interview.FirstQuestion,
    });
}).DisableAntiforgery();

app.MapPost("/api/interview/start", async (InterviewStartRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.JobDescriptionJson))
    {
        return Results.BadRequest(new { error = "Job description is required." });
    }

    if (string.IsNullOrWhiteSpace(request.ResumeJson))
    {
        return Results.BadRequest(new { error = "Resume is required." });
    }

    var agents = AgentFactory.Create(config.ChatClient);
    var workflow = new InterviewWorkflow(config, agents);
    var start = await workflow.StartInteractiveInterviewAsync(request.JobDescriptionJson, request.ResumeJson);

    return Results.Ok(new
    {
        message = start.Message,
        sessionId = start.SessionId,
        firstQuestion = start.FirstQuestion,
        audioBase64 = start.AudioBase64
    });
}).DisableAntiforgery();

app.MapPost("/api/interview/answer", async (InterviewAnswerRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.SessionId))
    {
        return Results.BadRequest(new { error = "SessionId is required." });
    }

    var agents = AgentFactory.Create(config.ChatClient);
    var workflow = new InterviewWorkflow(config, agents);

    try
    {
        var result = await workflow.AnswerInteractiveAsync(
            request.SessionId,
            request.Answer ?? string.Empty,
            request.PronunciationScore);

        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).DisableAntiforgery();

app.MapGet("/api/interview/report/{sessionId}", (string sessionId) =>
{
    var report = InterviewWorkflow.GetReport(sessionId);
    return report != null ? Results.Ok(report) : Results.NotFound(new { error = "Report not available." });
});

app.MapGet("/api/interview/report/{sessionId}/pdf", (string sessionId) =>
{
    var pdf = InterviewWorkflow.GetReportPdf(sessionId);
    return pdf != null
        ? Results.File(pdf, "application/pdf", $"InterviewReport_{sessionId}.pdf")
        : Results.NotFound(new { error = "Report not available." });
});

app.Run();

internal sealed record WorkflowRequest(string Criteria, string JobDescriptionJson, string ResumeJson);

internal sealed record InterviewStartRequest(string Criteria, string JobDescriptionJson, string ResumeJson);

internal sealed record InterviewAnswerRequest(string SessionId, string Answer, double? PronunciationScore);
