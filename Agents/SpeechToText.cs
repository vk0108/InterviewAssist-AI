using DotNetEnv;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.PronunciationAssessment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class SpeechToText
{
    static SpeechToText()
    {
        try { Env.Load(); } catch { /* ignore */ }
    }

    private static readonly string? _speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
    private static readonly string? _speechEndpoint = Environment.GetEnvironmentVariable("SPEECH_TO_TEXT_ENDPOINT") ?? Environment.GetEnvironmentVariable("ENDPOINT");
    private static readonly string? _speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");
    private static readonly string _speechLanguage = Environment.GetEnvironmentVariable("SPEECH_LANGUAGE") ?? "en-US";

    public sealed class SpeechCaptureResult
    {
        public string? Text { get; init; }
        public double? PronunciationScore { get; init; }
    }

    // Listens continuously until 10 seconds of silence
    public static async Task<SpeechCaptureResult?> ListenOnceAsync()
    {
        if (string.IsNullOrWhiteSpace(_speechKey) || (string.IsNullOrWhiteSpace(_speechEndpoint) && string.IsNullOrWhiteSpace(_speechRegion)))
        {
            Console.WriteLine("[STT] Skipped: SPEECH_KEY plus SPEECH_TO_TEXT_ENDPOINT/SPEECH_REGION not set.");
            return null;
        }

        try
        {
            SpeechConfig speechConfig;
            if (!string.IsNullOrWhiteSpace(_speechRegion))
            {
                Console.WriteLine($"[STT] Using region={_speechRegion}");
                speechConfig = SpeechConfig.FromSubscription(_speechKey!, _speechRegion);
            }
            else
            {
                Console.WriteLine($"[STT] Using endpoint={_speechEndpoint}");
                speechConfig = SpeechConfig.FromEndpoint(new Uri(_speechEndpoint!), _speechKey);
            }

            speechConfig.SpeechRecognitionLanguage = _speechLanguage;

            // Disable all SDK-level silence timeouts — our own 10s timer is the sole cutoff
            speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "600000");
            speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "600000");
            speechConfig.SetProperty("Speech_SegmentationSilenceTimeoutMs", "5000");

            using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            // Pronunciation assessment (continuous mode)
            var pronConfig = new PronunciationAssessmentConfig(
                referenceText: "",
                gradingSystem: GradingSystem.HundredMark,
                granularity: Granularity.Phoneme,
                enableMiscue: false);
            pronConfig.EnableProsodyAssessment();
            pronConfig.ApplyTo(recognizer);

            var sb = new StringBuilder();
            var pronScores = new List<double>();

            // Thread-safe timestamp: event handlers write from SDK threads, main thread reads
            long lastSpeechTicks = DateTime.UtcNow.Ticks;

            void TouchSpeechTimer()
            {
                Interlocked.Exchange(ref lastSpeechTicks, DateTime.UtcNow.Ticks);
            }

            recognizer.Recognizing += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Result?.Text))
                {
                    Console.WriteLine($"[STT] ...hearing: {e.Result.Text}");
                    TouchSpeechTimer();
                }
            };

            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    Console.WriteLine($"[STT] Segment: {e.Result.Text}");
                    TouchSpeechTimer();
                    sb.AppendLine(e.Result.Text);

                    var pronResult = PronunciationAssessmentResult.FromResult(e.Result);
                    if (pronResult?.PronunciationScore is { } ps && ps > 0)
                    {
                        pronScores.Add(ps);
                        Console.WriteLine($"[STT] Pronunciation score: {ps:F1}");
                    }
                }
            };

            recognizer.Canceled += (s, e) =>
            {
                Console.WriteLine($"[STT] Canceled: {e.Reason}; ErrorDetails={e.ErrorDetails}");
                // Don't stop — let the loop handle it via the silence timer
            };

            recognizer.SessionStopped += async (s, e) =>
            {
                Console.WriteLine("[STT] Session stopped by SDK — restarting...");
                try { await recognizer.StartContinuousRecognitionAsync(); }
                catch { /* recognizer may be disposed during shutdown */ }
            };

            Console.WriteLine("[STT] Speak your answer now... (listening until 10s of silence)");
            await recognizer.StartContinuousRecognitionAsync();

            // Only exit when 10 continuous seconds pass with no speech activity
            while (true)
            {
                await Task.Delay(500);
                var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastSpeechTicks));
                if (elapsed.TotalSeconds >= 10)
                {
                    Console.WriteLine("[STT] 10s silence reached — stopping.");
                    break;
                }
            }

            try { await recognizer.StopContinuousRecognitionAsync(); } catch { }

            var text = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.WriteLine("[STT] No speech could be recognized.");
                return null;
            }

            double? avgPron = pronScores.Count > 0 ? pronScores.Average() : null;
            Console.WriteLine($"[STT] Final: {text}");
            if (avgPron.HasValue)
                Console.WriteLine($"[STT] Avg pronunciation score: {avgPron.Value:F1}");
            return new SpeechCaptureResult { Text = text, PronunciationScore = avgPron };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STT] Error: {ex.Message}");
            return null;
        }
    }
}
