using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lulu.Core.Contracts;
using Lulu.Core.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lulu.Windows;

public sealed class WhisperCppProvider : IWhisperCppProvider
{
    public async Task<TranscriptionResult> TranscribeAsync(
        string executablePath,
        string modelPath,
        RecordedAudio recording,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ValidateFile(executablePath, "whisper.cpp executable");
        ValidateFile(modelPath, "whisper.cpp Tiny model");
        if (recording.WavBytes.Length == 0)
        {
            throw new InvalidOperationException("whisper.cpp cannot transcribe an empty recording.");
        }

        var stopwatch = Stopwatch.StartNew();
        var workDirectory = CreateWorkDirectory("whisper");
        try
        {
            var inputPath = Path.Combine(workDirectory, "input.wav");
            var outputPrefix = Path.Combine(workDirectory, "transcript");
            await File.WriteAllBytesAsync(inputPath, recording.WavBytes, cancellationToken).ConfigureAwait(false);
            var result = await LocalProcess.RunAsync(
                    executablePath,
                    [
                        "-m", modelPath,
                        "-f", inputPath,
                        "-otxt",
                        "-of", outputPrefix,
                        "--no-timestamps",
                        "-l", "auto",
                        "-t", Math.Clamp(Environment.ProcessorCount / 2, 2, 8).ToString()
                    ],
                    cancellationToken)
                .ConfigureAwait(false);
            var outputPath = outputPrefix + ".txt";
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"whisper.cpp exited with code {result.ExitCode}. {LocalProcess.CleanError(result.StandardError)}");
            }

            var text = File.Exists(outputPath)
                ? (await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false)).Trim()
                : result.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(
                    "whisper.cpp finished but detected no speech. Check the microphone and Tiny model path.");
            }

            stopwatch.Stop();
            return new TranscriptionResult(text, "whisper.cpp", Path.GetFileName(modelPath), stopwatch.Elapsed);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    public async Task<ProviderTestResult> TestAsync(
        string executablePath,
        string modelPath,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ValidateFile(executablePath, "whisper.cpp executable");
            ValidateFile(modelPath, "whisper.cpp Tiny model");
            var result = await LocalProcess.RunAsync(executablePath, ["--help"], cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"whisper.cpp could not start (exit {result.ExitCode}). {LocalProcess.CleanError(result.StandardError)}");
            }

            stopwatch.Stop();
            return new ProviderTestResult(
                "whisper.cpp Tiny",
                true,
                $"whisper.cpp and {Path.GetFileName(modelPath)} are ready ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult("whisper.cpp Tiny", false, exception.Message, stopwatch.Elapsed);
        }
    }

    private static void ValidateFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The {label} was not found at '{path}'. Set its path in Lulu Setup.");
        }
    }

    private static string CreateWorkDirectory(string component)
    {
        var path = Path.Combine(Path.GetTempPath(), "Lulu", component, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // A locked temporary file is harmless and will be cleared by Windows.
        }
    }
}

public sealed class PiperProvider : IPiperProvider
{
    public async Task<SpeechAudio?> SynthesizeSpeechAsync(
        string executablePath,
        string voiceModelPath,
        string text,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(executablePath, "Piper executable");
        ValidateFile(voiceModelPath, "Piper voice model");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var workDirectory = Path.Combine(Path.GetTempPath(), "Lulu", "piper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            var outputPath = Path.Combine(workDirectory, "speech.wav");
            var result = await LocalProcess.RunAsync(
                    executablePath,
                    ["-m", voiceModelPath, "-f", outputPath, "--", text.Trim()],
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Piper could not generate speech (exit {result.ExitCode}). {LocalProcess.CleanError(result.StandardError)}");
            }

            var wav = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
            return WaveAudioDecoder.Decode(wav, "Piper");
        }
        finally
        {
            WhisperCppProvider.TryDeleteDirectory(workDirectory);
        }
    }

    public async Task<ProviderTestResult> TestAsync(
        string executablePath,
        string voiceModelPath,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var audio = await SynthesizeSpeechAsync(
                    executablePath,
                    voiceModelPath,
                    "Lulu local voice is ready.",
                    cancellationToken)
                .ConfigureAwait(false);
            if (audio is null || audio.PcmData.Length == 0)
            {
                throw new InvalidOperationException("Piper returned no audio.");
            }

            stopwatch.Stop();
            return new ProviderTestResult(
                "Piper",
                true,
                $"Piper and {Path.GetFileName(voiceModelPath)} are ready ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult("Piper", false, exception.Message, stopwatch.Elapsed);
        }
    }

    private static void ValidateFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"The {label} was not found at '{path}'. Set its path in Lulu Setup.");
        }
    }
}

public sealed class ChatterboxNanoProvider : IChatterboxNanoProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public ChatterboxNanoProvider(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    }

    public async Task<SpeechAudio?> SynthesizeSpeechAsync(
        string endpoint,
        string model,
        string voice,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var root = ValidateLoopbackEndpoint(endpoint);
        var payload = JsonSerializer.Serialize(new
        {
            model = string.IsNullOrWhiteSpace(model) ? "chatterbox-nano" : model.Trim(),
            voice = string.IsNullOrWhiteSpace(voice) ? "default" : voice.Trim(),
            input = text.Trim(),
            response_format = "wav"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, "audio/speech"));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException(
                $"Chatterbox-Nano returned HTTP {(int)response.StatusCode}. {LocalProcess.CleanError(detail)}");
        }

        return bytes.Length == 0 ? null : WaveAudioDecoder.Decode(bytes, "Chatterbox-Nano");
    }

    public async Task<ProviderTestResult> TestAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var root = ValidateLoopbackEndpoint(endpoint);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "models"));
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Chatterbox-Nano returned HTTP {(int)response.StatusCode}.");
            }

            stopwatch.Stop();
            return new ProviderTestResult(
                "Chatterbox-Nano",
                true,
                $"The local Chatterbox-Nano server is ready ({stopwatch.Elapsed.TotalSeconds:0.0}s).",
                stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new ProviderTestResult("Chatterbox-Nano", false, exception.Message, stopwatch.Elapsed);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "Lulu could not reach the local Chatterbox-Nano server. Start it and confirm the loopback address in Setup.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The local Chatterbox-Nano server timed out.", exception);
        }
    }

    private static Uri ValidateLoopbackEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !uri.IsLoopback)
        {
            throw new InvalidOperationException(
                "Chatterbox-Nano must use a local HTTP loopback address such as http://127.0.0.1:4123/v1.");
        }

        return uri;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

internal static class WaveAudioDecoder
{
    internal static SpeechAudio Decode(byte[] wavBytes, string provider)
    {
        try
        {
            using var stream = new MemoryStream(wavBytes, false);
            using var reader = new WaveFileReader(stream);
            var pcm = new SampleToWaveProvider16(reader.ToSampleProvider());
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = pcm.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
            }

            return new SpeechAudio(
                output.ToArray(),
                pcm.WaveFormat.SampleRate,
                pcm.WaveFormat.Channels,
                16,
                $"audio/pcm;provider={provider}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException($"{provider} returned audio Lulu could not decode as WAV.", exception);
        }
    }
}

internal static class LocalProcess
{
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    internal static async Task<Result> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Windows could not start '{executablePath}'.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new Result(
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            TryKill(process);
            throw new InvalidOperationException($"Windows could not run '{executablePath}'. {exception.Message}", exception);
        }
    }

    internal static string CleanError(string? value)
    {
        var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
        {
            return "No additional details were returned.";
        }

        return text.Length <= 400 ? text : text[..400] + "...";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }
}
