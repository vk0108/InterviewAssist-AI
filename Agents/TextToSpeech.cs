using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using DotNetEnv;

public static class TextToSpeech
{
    static TextToSpeech()
    {
        try { Env.Load(); } catch { /* ignore */ }
    }

    private static readonly string? _speechKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
    private static readonly string? _speechEndpoint = Environment.GetEnvironmentVariable("TEXT_TO_SPEECH_ENDPOINT") ?? Environment.GetEnvironmentVariable("ENDPOINT");
    private static readonly string? _speechRegion = Environment.GetEnvironmentVariable("SPEECH_REGION");
    private static readonly string _voice = Environment.GetEnvironmentVariable("SPEECH_VOICE") ?? "hi-IN-KunalNeural";

    public static async Task<string?> SynthesizeToBase64Async(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (string.IsNullOrWhiteSpace(_speechKey) || (string.IsNullOrWhiteSpace(_speechEndpoint) && string.IsNullOrWhiteSpace(_speechRegion)))
            return null;

        try
        {
            SpeechConfig speechConfig;
            if (!string.IsNullOrWhiteSpace(_speechRegion))
                speechConfig = SpeechConfig.FromSubscription(_speechKey!, _speechRegion);
            else
                speechConfig = SpeechConfig.FromEndpoint(new Uri(_speechEndpoint!), _speechKey);

            speechConfig.SpeechSynthesisVoiceName = _voice;
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);

            using var synthesizer = new SpeechSynthesizer(speechConfig, null);
            var result = await synthesizer.SpeakTextAsync(text);
            if (result.Reason == ResultReason.SynthesizingAudioCompleted && result.AudioData?.Length > 0)
                return Convert.ToBase64String(result.AudioData);

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] SynthesizeToBase64 error: {ex.Message}");
            return null;
        }
    }

    public static async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (string.IsNullOrWhiteSpace(_speechKey) || (string.IsNullOrWhiteSpace(_speechEndpoint) && string.IsNullOrWhiteSpace(_speechRegion)))
        {
            Console.WriteLine("[TTS] Skipped: SPEECH_KEY plus SPEECH_ENDPOINT or SPEECH_REGION not set.");
            return;
        }

        try
        {
            SpeechConfig speechConfig;
            if (!string.IsNullOrWhiteSpace(_speechRegion))
            {
                speechConfig = SpeechConfig.FromSubscription(_speechKey!, _speechRegion);
            }
            else
            {
                speechConfig = SpeechConfig.FromEndpoint(new Uri(_speechEndpoint!), _speechKey);
            }

            speechConfig.SpeechSynthesisVoiceName = _voice;

            using var audioConfig = AudioConfig.FromDefaultSpeakerOutput();
            using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig);
            var result = await synthesizer.SpeakTextAsync(text);
            if (result.Reason != ResultReason.SynthesizingAudioCompleted)
            {
                var cancel = SpeechSynthesisCancellationDetails.FromResult(result);
            }
            else
            {
                var wavPath = Environment.GetEnvironmentVariable("SPEECH_OUTPUT_WAV");
                if (!string.IsNullOrWhiteSpace(wavPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(wavPath);
                        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        var stream = AudioDataStream.FromResult(result);
                        await stream.SaveToWaveFileAsync(wavPath);
                    }
                    catch (Exception saveEx)
                    {
                        Console.WriteLine($"[TTS] Failed to save WAV: {saveEx.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Error: {ex.Message}");
        }
    }
}


