using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Azure.AI.DocumentIntelligence;

public sealed class InterviewWorkflow
{
    private readonly EnvironmentConfig _config;
    private readonly InterviewAgents _agents;

    public InterviewWorkflow(EnvironmentConfig config, InterviewAgents agents)
    {
        _config = config;
        _agents = agents;
    }

    public async Task RunAsync()
    {
        var jobDescriptionJson = string.Empty;

        Console.WriteLine("Set the criteria for screening (Strong Match / Partial Match) : ");
        var verdictCriteria = (Console.ReadLine() ?? "partial match").Trim();

        var jdFilePath = Environment.GetEnvironmentVariable("JD_FILE_PATH");
        if (!string.IsNullOrWhiteSpace(jdFilePath) && File.Exists(jdFilePath))
        {
            try
            {
                jobDescriptionJson = await DocumentExtractionService.ExtractJdJsonAsync(_config.DocIntelClient, _config.ChatClient, jdFilePath);
                Console.WriteLine($"JD extracted from file: {jdFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"JD extraction failed: {ex.Message}. JD content will remain empty.");
            }
        }
        else
        {
            Console.WriteLine("No JD file provided; set JD_FILE_PATH to a valid path to extract the job description.");
        }

        var resumeJson = string.Empty;

        var resumeFilePath = Environment.GetEnvironmentVariable("RESUME_FILE_PATH");
        if (!string.IsNullOrWhiteSpace(resumeFilePath) && File.Exists(resumeFilePath))
        {
            try
            {
                resumeJson = await DocumentExtractionService.ExtractResumeJsonAsync(_config.DocIntelClient, _config.ChatClient, resumeFilePath);
                Console.WriteLine($"Resume extracted from file: {resumeFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Resume extraction failed: {ex.Message}. Resume content will remain empty.");
            }
        }
        else
        {
            Console.WriteLine("No resume file provided; set RESUME_FILE_PATH to a valid path to extract the resume.");
        }

        var candidateName = ExtractCandidateName(resumeJson) 
            ?? Environment.GetEnvironmentVariable("CANDIDATE_NAME") 
            ?? "Candidate";

        Console.WriteLine($"JD JSON: {jobDescriptionJson}");
        Console.WriteLine($"Resume JSON: {resumeJson}");

        var screening = await RunScreeningAsync(jobDescriptionJson, resumeJson, verdictCriteria);
        Console.WriteLine(screening.ScreeningText);

        if (screening.VerdictNo || !screening.PassesCriteria)
        {
            Console.WriteLine("Interview not started because the candidate did not pass screening per criteria.");
            return;
        }

        var interviewThread = _agents.Interviewer.GetNewThread();

        Console.WriteLine("Do you want to proceed with the interview? (yes/no)");
        var proceedAnswer = Console.ReadLine();
        if (proceedAnswer == "no" ){
          return;
        }

        var interviewerMessages = new List<ChatMessage>
        {
          new(ChatRole.User, "Start the technical interview. Ask exactly one question at a time based on the JD and resume. Begin with a primary-skill question. The interview is adaptive (3-15 questions based on performance)."),
          new(ChatRole.User, jobDescriptionJson),
          new(ChatRole.User, resumeJson)
        };

        var interviewResult = await _agents.Interviewer.RunAsync(interviewerMessages, thread: interviewThread, options: null, cancellationToken: default);
        var interviewText = ExtractResponseText(interviewResult);

        Console.WriteLine(interviewText);
        await TextToSpeech.SpeakAsync(interviewText);

        var scores = new List<int>();
        var englishScores = new List<double>();
        var currentQuestion = interviewText;
        var exchangeHistory = new List<string>();
        var endingAfterFinalAnswer = false;
        var finalQuestionIssued = false;
        var followUpTriggerCount = 0;

        while (true)
        {
          Console.WriteLine();
          Console.Write("Candidate (speak or type): ");
          var speechResult = await SpeechToText.ListenOnceAsync();
          var answer = speechResult?.Text ?? Console.ReadLine();
          if (speechResult?.PronunciationScore is { } score)
          {
              englishScores.Add(score);
          }
          if (string.IsNullOrWhiteSpace(answer))
          {
            Console.WriteLine("Interview ended by user.");
            break;
          }

          var latestAnswerForManager = answer;

          // Initial evaluation on the main answer
          var evalText = await EvaluateAsync(jobDescriptionJson, resumeJson, currentQuestion, latestAnswerForManager);
          var scoreValue = ExtractScore(evalText);

          // If weak (<3.5) and not the final question, ask follow-ups, evaluate each individually, and average
          if (scoreValue >= 0 && scoreValue < 3.5 && !finalQuestionIssued)
          {
            followUpTriggerCount++;
            var (lowScoreFollowUps, followUpScores) = await CollectLowScoreFollowUpsWithScoresAsync(jobDescriptionJson, resumeJson, currentQuestion, latestAnswerForManager, interviewThread, englishScores);
            if (lowScoreFollowUps is null)
            {
              Console.WriteLine("Interview ended by user during low-score follow-ups.");
              break;
            }

            latestAnswerForManager = $"{latestAnswerForManager}\nFollow-up answers:\n{lowScoreFollowUps}";

            if (followUpScores.Count > 0)
            {
              scoreValue = (int)Math.Round(followUpScores.Average());
              evalText = $"Score: {scoreValue} — Follow-up average (individual follow-up scores: {string.Join(", ", followUpScores)})";
            }
          }

          if (scoreValue >= 0)
          {
            scores.Add(scoreValue);
          }

          Console.WriteLine(evalText);
          Console.WriteLine();

          exchangeHistory.Add($"Question {scores.Count}: {currentQuestion}\nAnswer: {latestAnswerForManager}\nEvaluation: {evalText}");

          if (endingAfterFinalAnswer)
          {
            Console.WriteLine("Interview completed after final question.");
            break;
          }

          var managerMessages = new List<ChatMessage>
          {
            new(ChatRole.User, jobDescriptionJson),
            new(ChatRole.User, resumeJson),
            new(ChatRole.User, $"Latest Question: {currentQuestion}"),
            new(ChatRole.User, $"Latest Answer: {latestAnswerForManager}"),
            new(ChatRole.User, $"Evaluation: {evalText}"),
            new(ChatRole.User, $"Scores so far: {string.Join(", ", scores)}"),
            new(ChatRole.User, $"Questions asked: {scores.Count}"),
            new(ChatRole.User, $"Follow-up rounds triggered: {followUpTriggerCount} out of {scores.Count} questions (high ratio = candidate struggling)"),
            new(ChatRole.User, $"Interview history so far:\n{string.Join("\n---\n", exchangeHistory)}")
          };

          var managerResult = await _agents.Manager.RunAsync(managerMessages, thread: null, options: null, cancellationToken: default);
          var managerText = ExtractResponseText(managerResult);

          var (decision, _) = ParseManagerDecision(managerText, scores);
          var managerIndicatesFinal = IndicatesFinalQuestion(managerText);

          if (!finalQuestionIssued && (decision is "terminate" or "complete" || managerIndicatesFinal))
          {
            finalQuestionIssued = true;
            endingAfterFinalAnswer = true;

            var finalPrompt = "Ask one final question now, and casually mention it's the last question (e.g., 'Before we wrap, one last question: ...'). Do not include any other closing or thank-you statements; output only the final question.";
            var followUpMessagesFinal = new List<ChatMessage>
            {
              new(ChatRole.User, finalPrompt)
            };
            var followUpResultFinal = await _agents.Interviewer.RunAsync(followUpMessagesFinal, thread: interviewThread, options: null, cancellationToken: default);
            var followUpTextFinal = ExtractResponseText(followUpResultFinal);

            Console.WriteLine(followUpTextFinal);
            await TextToSpeech.SpeakAsync(followUpTextFinal);
            currentQuestion = followUpTextFinal;
            continue;
          }

          if (decision is "terminate" or "complete")
          {
            endingAfterFinalAnswer = true;
            continue;
          }

          var followUpMessages = new List<ChatMessage>
          {
            new(ChatRole.User, answer),
            new(ChatRole.User, $"Interview history so far:\n{string.Join("\n---\n", exchangeHistory)}")
          };

          var followUpResult = await _agents.Interviewer.RunAsync(followUpMessages, thread: interviewThread, options: null, cancellationToken: default);
          var followUpText = ExtractResponseText(followUpResult);

          Console.WriteLine(followUpText);
          await TextToSpeech.SpeakAsync(followUpText);
          currentQuestion = followUpText;
        }

        if (scores.Count > 0)
        {
          var average = scores.Average();
          Console.WriteLine($"\nAverage Score: {average:F2} / 5 across {scores.Count} questions.");
            var finalVerdict = new List<ChatMessage>
            {
            new(ChatRole.User, $"Average Score: {average:F2} / 5"),
            new(ChatRole.User, $"Scores: {string.Join(", ", scores)}; Total questions: {scores.Count}"),
            new(ChatRole.User, $"Interview history:\n{string.Join("\n---\n", exchangeHistory)}"),
            new(ChatRole.User, "Based on the interview performance and scores, provide a final recommendation on whether to proceed with the candidate or not. Consider the average score, consistency, and overall fit to the job description.")
            };
            var finalResult = await _agents.Manager.RunAsync(finalVerdict, thread: null, options: null, cancellationToken: default);
            var finalText = ExtractResponseText(finalResult);
            Console.WriteLine(finalText);

            var outputPath = Environment.GetEnvironmentVariable("REPORT_OUTPUT_PATH") ?? "InterviewReport.pdf";
            var englishScore = englishScores.Count > 0 ? englishScores.Average() : (double?)null;
            await ConvoSumm.GenerateAsync(_config, exchangeHistory, scores, finalText, candidateName, outputPath, englishScore);
        }
        else
        {
            Console.WriteLine("No scores recorded; interview incomplete.");
        }
    }

    #region Interactive (API-driven) workflow state

    private sealed class InterviewSessionState
    {
        public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
        public string JobDescriptionJson { get; init; } = string.Empty;
        public string ResumeJson { get; init; } = string.Empty;
        public InterviewAgents Agents { get; init; } = default!;
        public AgentThread InterviewThread { get; init; } = default!;
        public string CurrentQuestion { get; set; } = string.Empty;
        public List<int> Scores { get; } = new();
        public List<double> EnglishScores { get; } = new();
        public List<string> ExchangeHistory { get; } = new();
        public bool FinalQuestionIssued { get; set; }
        public bool EndingAfterFinalAnswer { get; set; }
        public bool Completed { get; set; }
        public string ManagerAssessment { get; set; } = string.Empty;
        public bool AwaitingFollowUps { get; set; }
        public List<string> PendingFollowUpQuestions { get; set; } = new();
        public List<string> CollectedFollowUpAnswers { get; set; } = new();
        public string? PendingWeakAnswer { get; set; }
        public string? PendingScoredQuestion { get; set; }
        public string? PendingEvalText { get; set; }
        public int PendingScoreValue { get; set; } = -1;
        public int FollowUpTriggerCount { get; set; }
        public List<int> PendingFollowUpScores { get; set; } = new();
        public ConvoSumm.ReportPayload? ReportPayload { get; set; }
        public string CandidateName { get; set; } = "Candidate";
    }

    private static readonly ConcurrentDictionary<string, InterviewSessionState> Sessions = new();

    public sealed record InteractiveStartResponse(string SessionId, string FirstQuestion, string Message, string? AudioBase64);

    public sealed record InteractiveAnswerResponse(
        string SessionId,
        string Status,
        string? Evaluation,
        string? ManagerAssessment,
        string? NextQuestion,
        bool AwaitingFollowUps,
        IReadOnlyList<string>? FollowUpQuestions,
        bool Completed,
        double? AverageScore,
        int TotalQuestions,
        string? FinalRecommendation,
        string? AudioBase64);

    public async Task<InteractiveStartResponse> StartInteractiveInterviewAsync(string jobDescriptionJson, string resumeJson)
    {
        if (string.IsNullOrWhiteSpace(jobDescriptionJson))
        {
            throw new ArgumentException("Job description is required.", nameof(jobDescriptionJson));
        }

        var (interviewThread, interviewText) = await BeginInterviewAsync(jobDescriptionJson, resumeJson);
        var audioBase64 = await TextToSpeech.SynthesizeToBase64Async(interviewText);

        var session = new InterviewSessionState
        {
            JobDescriptionJson = jobDescriptionJson,
            ResumeJson = resumeJson ?? string.Empty,
            Agents = _agents,
            InterviewThread = interviewThread,
            CurrentQuestion = interviewText
        };

        Sessions[session.SessionId] = session;

        return new InteractiveStartResponse(session.SessionId, interviewText, "Interview started.", audioBase64);
    }

    public async Task<InteractiveAnswerResponse> AnswerInteractiveAsync(
        string sessionId,
        string answer,
        double? pronunciationScore)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !Sessions.TryGetValue(sessionId, out var session))
        {
            throw new ArgumentException("Invalid session id.", nameof(sessionId));
        }

        var agents = session.Agents;

        if (session.Completed)
        {
            return new InteractiveAnswerResponse(session.SessionId, "Interview already completed.", null, session.ManagerAssessment, null, false, null, true, AverageOrNull(session.Scores), session.Scores.Count, session.ManagerAssessment, null);
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            var speechResult = await SpeechToText.ListenOnceAsync();
            answer = speechResult?.Text ?? string.Empty;
            if (speechResult?.PronunciationScore is { } pScoreListen)
            {
                session.EnglishScores.Add(pScoreListen);
            }
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return new InteractiveAnswerResponse(session.SessionId, "Answer required.", null, null, session.CurrentQuestion, session.AwaitingFollowUps, session.PendingFollowUpQuestions, session.Completed, AverageOrNull(session.Scores), session.Scores.Count, null, null);
        }

        if (pronunciationScore is { } pScore)
        {
            session.EnglishScores.Add(pScore);
        }

        string evalText;
        int scoreValue;
        var followUpJustCompleted = false;
        string? followUpOriginalQuestion = null;
        string? followUpAggregatedAnswer = null;

        if (session.AwaitingFollowUps)
        {
            session.CollectedFollowUpAnswers.Add(answer);

            // Evaluate this follow-up answer immediately
            var followUpIdx = session.CollectedFollowUpAnswers.Count - 1;
            var followUpQ = session.PendingFollowUpQuestions[followUpIdx];
            var followUpEvalText = await EvaluateAsync(session.JobDescriptionJson, session.ResumeJson, followUpQ, answer, agents);
            var followUpScoreVal = ExtractScore(followUpEvalText);
            if (followUpScoreVal >= 0)
                session.PendingFollowUpScores.Add(followUpScoreVal);

            // Recovery check: if the candidate scored >=3.5 on this follow-up, skip remaining
            // and continue to the next main question. Otherwise, if we haven't hit the 3-follow-up
            // limit, generate the next follow-up on-demand.
            var recovered = followUpScoreVal >= 0 && followUpScoreVal >= 3.5;
            var asked = session.CollectedFollowUpAnswers.Count;
            var canAskMore = asked < FollowUpsToAsk;

            if (!recovered && canAskMore)
            {
                var nextFollow = await AskSingleFollowUpQuestionAsync(
                    agents,
                    session.InterviewThread,
                    session.JobDescriptionJson,
                    session.ResumeJson,
                    session.PendingScoredQuestion ?? session.CurrentQuestion,
                    session.PendingWeakAnswer ?? answer);
                session.PendingFollowUpQuestions.Add(nextFollow);
                session.CurrentQuestion = nextFollow;
                var nextAudio = await TextToSpeech.SynthesizeToBase64Async(nextFollow);

                return new InteractiveAnswerResponse(
                    session.SessionId,
                    "Next follow-up question.",
                    followUpEvalText,
                    null,
                    nextFollow,
                    true,
                    session.PendingFollowUpQuestions,
                    false,
                    AverageOrNull(session.Scores),
                    session.Scores.Count,
                    null,
                    nextAudio);
            }

            // Recovered or hit the 3-follow-up limit — average collected follow-up scores
            // as the new score for this main question and move on.
            var segments = session.PendingFollowUpQuestions.Select((q, i) => $"Follow-up {i + 1}: {q}\nAnswer: {session.CollectedFollowUpAnswers[i]}");
            var aggregated = $"{session.PendingWeakAnswer}\nFollow-up answers:\n{string.Join("\n", segments)}";

            var avgFollowUpScore = session.PendingFollowUpScores.Count > 0
                ? (int)Math.Round(session.PendingFollowUpScores.Average())
                : session.PendingScoreValue;
            scoreValue = avgFollowUpScore;
            evalText = $"Score: {avgFollowUpScore} — Follow-up average (individual follow-up scores: {string.Join(", ", session.PendingFollowUpScores)})";

            followUpJustCompleted = true;
            followUpOriginalQuestion = session.PendingScoredQuestion ?? session.CurrentQuestion;
            followUpAggregatedAnswer = aggregated;

            session.AwaitingFollowUps = false;
            session.PendingFollowUpQuestions.Clear();
            session.CollectedFollowUpAnswers.Clear();
            session.PendingWeakAnswer = null;
            session.PendingScoredQuestion = null;
            session.PendingEvalText = null;
            session.PendingScoreValue = -1;
            session.PendingFollowUpScores.Clear();
        }
        else
        {
            evalText = await EvaluateAsync(session.JobDescriptionJson, session.ResumeJson, session.CurrentQuestion, answer, agents);
            scoreValue = ExtractScore(evalText);
        }

        // Trigger follow-ups for weak answers — generate only the first one; subsequent
        // follow-ups are produced on-demand based on whether the candidate recovers.
        if (scoreValue >= 0 && scoreValue < 3.5 && !session.FinalQuestionIssued && !session.AwaitingFollowUps && !followUpJustCompleted)
        {
            var firstFollow = await AskSingleFollowUpQuestionAsync(
                agents,
                session.InterviewThread,
                session.JobDescriptionJson,
                session.ResumeJson,
                session.CurrentQuestion,
                answer);

            session.PendingFollowUpQuestions = new List<string> { firstFollow };
            session.CollectedFollowUpAnswers.Clear();
            session.PendingWeakAnswer = answer;
            session.PendingScoredQuestion = session.CurrentQuestion;
            session.PendingEvalText = evalText;
            session.PendingScoreValue = scoreValue;
            session.PendingFollowUpScores.Clear();
            session.FollowUpTriggerCount++;
            session.AwaitingFollowUps = true;

            session.CurrentQuestion = firstFollow;
            var firstFollowAudio = await TextToSpeech.SynthesizeToBase64Async(firstFollow);

            return new InteractiveAnswerResponse(
                session.SessionId,
                "Follow-up question generated.",
                evalText,
                null,
                firstFollow,
                true,
                session.PendingFollowUpQuestions,
                false,
                AverageOrNull(session.Scores),
                session.Scores.Count,
                null,
                firstFollowAudio);
        }

        // Add score: either averaged follow-up score (followUpJustCompleted) or normal evaluation score.
        // Follow-up trigger path returns early above, so this is never reached for deferred scores.
        if (scoreValue >= 0)
        {
            session.Scores.Add(scoreValue);
        }

        var historyQuestion = followUpJustCompleted ? (followUpOriginalQuestion ?? session.CurrentQuestion) : session.CurrentQuestion;
        var historyAnswer = followUpJustCompleted ? (followUpAggregatedAnswer ?? answer) : answer;
        session.ExchangeHistory.Add($"Question {session.Scores.Count}: {historyQuestion}\nAnswer: {historyAnswer}\nEvaluation: {evalText}");


        if (session.EndingAfterFinalAnswer)
        {
            await CompleteAndSummarizeAsync(session);
            return new InteractiveAnswerResponse(
                session.SessionId,
                "Interview completed after final question.",
                evalText,
                session.ManagerAssessment,
                null,
                false,
                null,
                true,
                session.Scores.Average(),
                session.Scores.Count,
                session.ManagerAssessment,
                null);
        }

        var managerMessages = new List<ChatMessage>
        {
            new(ChatRole.User, session.JobDescriptionJson),
            new(ChatRole.User, session.ResumeJson),
            new(ChatRole.User, $"Latest Question: {session.CurrentQuestion}"),
            new(ChatRole.User, $"Latest Answer: {answer}"),
            new(ChatRole.User, $"Evaluation: {evalText}"),
            new(ChatRole.User, $"Scores so far: {string.Join(", ", session.Scores)}"),
            new(ChatRole.User, $"Questions asked: {session.Scores.Count}"),
            new(ChatRole.User, $"Follow-up rounds triggered: {session.FollowUpTriggerCount} out of {session.Scores.Count} questions (high ratio = candidate struggling)"),
            new(ChatRole.User, $"Interview history so far:\n{string.Join("\n---\n", session.ExchangeHistory)}")
        };

        var managerResult = await agents.Manager.RunAsync(managerMessages, thread: null, options: null, cancellationToken: default);
        var managerText = ExtractResponseText(managerResult);

        session.ManagerAssessment = managerText;

        var (decision, _) = ParseManagerDecision(managerText, session.Scores);
        var managerIndicatesFinal = IndicatesFinalQuestion(managerText);

        if (!session.FinalQuestionIssued && (decision is "terminate" or "complete" || managerIndicatesFinal))
        {
            session.FinalQuestionIssued = true;
            session.EndingAfterFinalAnswer = true;

            var finalPrompt = "Ask one final question now, and casually mention it's the last question (e.g., 'Before we wrap, one last question: ...'). Do not include any other closing or thank-you statements; output only the final question.";
            var followUpMessagesFinal = new List<ChatMessage>
            {
                new(ChatRole.User, finalPrompt)
            };
            var followUpResultFinal = await agents.Interviewer.RunAsync(followUpMessagesFinal, thread: session.InterviewThread, options: null, cancellationToken: default);
            var followUpTextFinal = ExtractResponseText(followUpResultFinal);

            session.CurrentQuestion = followUpTextFinal;
            var finalAudio = await TextToSpeech.SynthesizeToBase64Async(followUpTextFinal);

            return new InteractiveAnswerResponse(
                session.SessionId,
                "Final question issued.",
                evalText,
                managerText,
                followUpTextFinal,
                false,
                null,
                false,
                AverageOrNull(session.Scores),
                session.Scores.Count,
                null,
                finalAudio);
        }

        if (decision is "terminate" or "complete")
        {
            session.EndingAfterFinalAnswer = true;
            return new InteractiveAnswerResponse(
                session.SessionId,
                "Awaiting final answer before completion.",
                evalText,
                managerText,
                null,
                false,
                null,
                false,
                AverageOrNull(session.Scores),
                session.Scores.Count,
                null,
                null);
        }

        var followUpMessages = new List<ChatMessage>
        {
            new(ChatRole.User, answer),
            new(ChatRole.User, $"Interview history so far:\n{string.Join("\n---\n", session.ExchangeHistory)}")
        };

        var followUpResult = await agents.Interviewer.RunAsync(followUpMessages, thread: session.InterviewThread, options: null, cancellationToken: default);
        var followUpText = ExtractResponseText(followUpResult);

        session.CurrentQuestion = followUpText;
        var followUpAudio = await TextToSpeech.SynthesizeToBase64Async(followUpText);

        return new InteractiveAnswerResponse(
            session.SessionId,
            "Next question generated.",
            evalText,
            managerText,
            followUpText,
            false,
            null,
            false,
            AverageOrNull(session.Scores),
            session.Scores.Count,
            null,
            followUpAudio);
    }

    private async Task CompleteAndSummarizeAsync(InterviewSessionState session)
    {
        session.Completed = true;
        if (session.Scores.Count == 0)
        {
            session.ManagerAssessment = "No scores recorded; interview incomplete.";
            return;
        }

        var average = session.Scores.Average();
        var finalVerdict = new List<ChatMessage>
        {
            new(ChatRole.User, $"Average Score: {average:F2} / 5"),
            new(ChatRole.User, $"Scores: {string.Join(", ", session.Scores)}; Total questions: {session.Scores.Count}"),
            new(ChatRole.User, $"Interview history:\n{string.Join("\n---\n", session.ExchangeHistory)}"),
            new(ChatRole.User, "Based on the interview performance and scores, provide a final recommendation on whether to proceed with the candidate or not. Consider the average score, consistency, and overall fit to the job description.")
        };
        var finalResult = await session.Agents.Manager.RunAsync(finalVerdict, thread: null, options: null, cancellationToken: default);
        var finalText = ExtractResponseText(finalResult);

        session.ManagerAssessment = finalText;

        var candidateName = ExtractCandidateName(session.ResumeJson) ?? "Candidate";
        session.CandidateName = candidateName;
        var outputPath = Environment.GetEnvironmentVariable("REPORT_OUTPUT_PATH") ?? "InterviewReport.pdf";
        var englishScore = session.EnglishScores.Count > 0 ? session.EnglishScores.Average() : (double?)null;
        session.ReportPayload = await ConvoSumm.GenerateAsync(_config, session.ExchangeHistory, session.Scores, finalText, candidateName, outputPath, englishScore);
    }

    public static object? GetReport(string sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session) || !session.Completed)
            return null;

        return new
        {
            candidateName = session.CandidateName,
            averageScore = session.Scores.Count > 0 ? session.Scores.Average() : 0,
            totalQuestions = session.Scores.Count,
            scores = session.Scores,
            managerAssessment = session.ManagerAssessment,
            report = session.ReportPayload
        };
    }

    public static byte[]? GetReportPdf(string sessionId)
    {
        if (!Sessions.TryGetValue(sessionId, out var session) || !session.Completed || session.ReportPayload == null)
            return null;

        return ReportGeneration.GeneratePdfBytes(session.CandidateName, session.ReportPayload, session.Scores);
    }

    private const int FollowUpsToAsk = 3;

    private static async Task<string> AskSingleFollowUpQuestionAsync(
        InterviewAgents agents,
        AgentThread interviewThread,
        string jobDescriptionJson,
        string resumeJson,
        string currentQuestion,
        string scoredAnswer)
    {
        var followUpMessages = new List<ChatMessage>
        {
            new(ChatRole.User, jobDescriptionJson),
            new(ChatRole.User, resumeJson),
            new(ChatRole.User, $"Original Question: {currentQuestion}"),
            new(ChatRole.User, $"Candidate's scored answer (score < 3.5): {scoredAnswer}"),
            new(ChatRole.User, "Ask one concise follow-up question to clarify or deepen understanding of the same topic. Keep it short, targeted, and do not evaluate.")
        };

        var followUpResult = await agents.Interviewer.RunAsync(followUpMessages, thread: interviewThread, options: null, cancellationToken: default);
        return ExtractResponseText(followUpResult);
    }

    #endregion

    public async Task<ScreeningResponse> RunScreeningAsync(string jobDescriptionJson, string resumeJson, string verdictCriteria)
    {
        if (string.IsNullOrWhiteSpace(jobDescriptionJson))
        {
            throw new ArgumentException("Job description is required.", nameof(jobDescriptionJson));
        }

        var criteriaValue = string.IsNullOrWhiteSpace(verdictCriteria) ? "partial match" : verdictCriteria.Trim();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, jobDescriptionJson),
            new(ChatRole.User, resumeJson ?? string.Empty)
        };

        var screeningResult = await _agents.Screening.RunAsync(messages, thread: null, options: null, cancellationToken: default);
        var screeningText = ExtractResponseText(screeningResult);

        var verdictLine = screeningText
            .Split('\n')
            .FirstOrDefault(l => l.TrimStart().StartsWith("Verdict", StringComparison.OrdinalIgnoreCase))
            ?? screeningText;

        var verdictStrong = verdictLine.Contains("Strong Match", StringComparison.OrdinalIgnoreCase);
        var verdictPartial = verdictLine.Contains("Partial Match", StringComparison.OrdinalIgnoreCase);
        var verdictNo = verdictLine.Contains("No Match", StringComparison.OrdinalIgnoreCase);
        var passesCriteria = CriteriaAllowsStrong(criteriaValue)
            ? verdictStrong
            : CriteriaAllowsPartialOrStrong(criteriaValue) && (verdictStrong || verdictPartial);

        return new ScreeningResponse(
            screeningText,
            verdictLine,
            verdictStrong,
            verdictPartial,
            verdictNo,
            passesCriteria);
    }

    private async Task<(AgentThread Thread, string FirstQuestion)> BeginInterviewAsync(string jobDescriptionJson, string resumeJson)
    {
        var interviewThread = _agents.Interviewer.GetNewThread();
        var interviewerMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Start the technical interview. Ask exactly one question at a time based on the JD and resume. Begin with a primary-skill question. The interview is adaptive (3-15 questions based on performance)."),
            new(ChatRole.User, jobDescriptionJson),
            new(ChatRole.User, resumeJson ?? string.Empty)
        };

        var interviewResult = await _agents.Interviewer.RunAsync(interviewerMessages, thread: interviewThread, options: null, cancellationToken: default);
        return (interviewThread, ExtractResponseText(interviewResult));
    }

    public async Task<InterviewStartResponse> StartInterviewAsync(string jobDescriptionJson, string resumeJson)
    {
        if (string.IsNullOrWhiteSpace(jobDescriptionJson))
        {
            throw new ArgumentException("Job description is required.", nameof(jobDescriptionJson));
        }

        var (interviewThread, interviewText) = await BeginInterviewAsync(jobDescriptionJson, resumeJson);
        return new InterviewStartResponse(interviewThread, interviewText);
    }

    public static void MapUploadEndpoint(WebApplication app, EnvironmentConfig config)
    {
        app.MapPost("/api/jd/upload", async (IFormFile file) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded." });
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{file.FileName}");

            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await file.CopyToAsync(stream);
                }

                var jdJson = await DocumentExtractionService.ExtractJdJsonAsync(
                    config.DocIntelClient,
                    config.ChatClient,
                    tempPath);

                return Results.Ok(new { jdJson });
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }).DisableAntiforgery();
    }

    public static void MapResumeUploadEndpoint(WebApplication app, EnvironmentConfig config)
    {
        app.MapPost("/api/resume/upload", async (IFormFile file) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded." });
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{file.FileName}");

            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await file.CopyToAsync(stream);
                }

                var resumeJson = await DocumentExtractionService.ExtractResumeJsonAsync(
                    config.DocIntelClient,
                    config.ChatClient,
                    tempPath);

                return Results.Ok(new { resumeJson });
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }).DisableAntiforgery();
    }

    private static string? ExtractCandidateName(string resumeJson)
    {
        if (string.IsNullOrWhiteSpace(resumeJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(resumeJson);
            var name = FindNameInElement(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch
        {
            // fall back to regex below
        }

        var match = Regex.Match(resumeJson, "\\\"(?:name|candidate_name|full_name)\\\"\\s*:\\s*\\\"(?<name>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var name = match.Groups["name"].Value.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        return null;
    }

    private static string? FindNameInElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (IsNameProperty(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }

                    var nested = FindNameInElement(prop.Value);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindNameInElement(item);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                break;
        }

        return null;
    }

    private static bool IsNameProperty(string name) =>
        name.Equals("name", StringComparison.OrdinalIgnoreCase)
        || name.Equals("candidate_name", StringComparison.OrdinalIgnoreCase)
        || name.Equals("full_name", StringComparison.OrdinalIgnoreCase);

    private async Task<(string? Segments, List<int> Scores)> CollectLowScoreFollowUpsWithScoresAsync(
        string jobDescriptionJson,
        string resumeJson,
        string currentQuestion,
        string scoredAnswer,
        AgentThread interviewThread,
        List<double> englishScores)
    {
        var segments = new List<string>();
        var followUpScores = new List<int>();

        for (var i = 0; i < FollowUpsToAsk; i++)
        {
            var followUpQuestion = await AskSingleFollowUpQuestionAsync(_agents, interviewThread, jobDescriptionJson, resumeJson, currentQuestion, scoredAnswer);

            Console.WriteLine(followUpQuestion);
            await TextToSpeech.SpeakAsync(followUpQuestion);
            Console.Write("Candidate (follow-up, speak or type): ");
            var followUpSpeech = await SpeechToText.ListenOnceAsync();
            var followUpAnswer = followUpSpeech?.Text ?? Console.ReadLine();
            if (followUpSpeech?.PronunciationScore is { } followUpPronScore)
            {
                englishScores.Add(followUpPronScore);
            }
            if (string.IsNullOrWhiteSpace(followUpAnswer))
            {
                return (null, followUpScores);
            }

            // Evaluate each follow-up answer immediately
            var evalText = await EvaluateAsync(jobDescriptionJson, resumeJson, followUpQuestion ?? string.Empty, followUpAnswer);
            var scoreVal = ExtractScore(evalText);
            if (scoreVal >= 0)
                followUpScores.Add(scoreVal);

            segments.Add($"Follow-up {i + 1}: {followUpQuestion}\nAnswer: {followUpAnswer}\nEvaluation: {evalText}");

            // Early exit on recovery: if this follow-up was answered well (>=3.5), skip the rest.
            if (scoreVal >= 3.5)
            {
                break;
            }
        }

        return (string.Join("\n", segments), followUpScores);
    }

    private static bool CriteriaAllowsStrong(string verdictCriteria) => verdictCriteria.Equals("strong match", StringComparison.OrdinalIgnoreCase);

    private static bool CriteriaAllowsPartialOrStrong(string verdictCriteria) => verdictCriteria.Equals("partial match", StringComparison.OrdinalIgnoreCase) || CriteriaAllowsStrong(verdictCriteria);

    private async Task<string> EvaluateAsync(
        string jobDescriptionJson,
        string resumeJson,
        string currentQuestion,
        string aggregatedAnswer,
        InterviewAgents? agentsOverride = null)
    {
        var agents = agentsOverride ?? _agents;
        var evalMessages = new List<ChatMessage>
        {
            new(ChatRole.User, jobDescriptionJson),
            new(ChatRole.User, resumeJson),
            new(ChatRole.User, $"Question: {currentQuestion}"),
            new(ChatRole.User, $"Answer: {aggregatedAnswer}")
        };

        var evalResult = await agents.Evaluator.RunAsync(evalMessages, thread: null, options: null, cancellationToken: default);
        return ExtractResponseText(evalResult);
    }

    private static string ExtractResponseText(AgentRunResponse result)
    {
        return result.Messages?.LastOrDefault()?.Text
            ?? result.ToString()
            ?? string.Empty;
    }

    private static double? AverageOrNull(List<int> scores)
        => scores.Count > 0 ? scores.Average() : null;

    private static (string decision, bool allowTerminate) ParseManagerDecision(string managerText, List<int> scores)
    {
        var decisionMatch = Regex.Match(managerText ?? string.Empty, @"Decision:\s*(?<decision>\w+)", RegexOptions.IgnoreCase);
        var decision = decisionMatch.Success ? decisionMatch.Groups["decision"].Value.ToLowerInvariant() : "continue";

        var recentScores = scores.Skip(Math.Max(0, scores.Count - 3)).ToList();
        var recentLowCount = recentScores.Count(s => s <= 2);
        var recentAverage = recentScores.Count > 0 ? recentScores.Average() : 0;
        var allowTerminate = scores.Count >= 3 && (recentLowCount >= 2 || recentAverage < 2.5);

        if (decision is "terminate" && !allowTerminate)
            decision = "continue";

        return (decision, allowTerminate);
    }

    private static bool IndicatesFinalQuestion(string managerText)
        => managerText.Contains("final question", StringComparison.OrdinalIgnoreCase)
        || managerText.Contains("last question", StringComparison.OrdinalIgnoreCase);

    private static int ExtractScore(string evalText)
    {
        var match = Regex.Match(evalText ?? string.Empty, @"Score:\s*(?<score>[0-5])");
        return match.Success && int.TryParse(match.Groups["score"].Value, out var scoreValue)
            ? scoreValue
            : -1;
    }

    public sealed record ScreeningResponse(
        string ScreeningText,
        string VerdictLine,
        bool VerdictStrong,
        bool VerdictPartial,
        bool VerdictNo,
        bool PassesCriteria);

    public sealed record InterviewStartResponse(AgentThread Thread, string FirstQuestion);
}
