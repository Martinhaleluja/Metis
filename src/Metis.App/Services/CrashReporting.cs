using System;
using System.Collections.Generic;
using System.IO;
using Metis.Core.Services;
using Sentry;

namespace Metis.App.Services;

/// <summary>
/// Crash reporting, and off unless somebody deliberately turns it on.
///
/// Metis has no Sentry account, so no DSN is compiled in and none is read from
/// anything that ships. The DSN comes from the <c>METIS_SENTRY_DSN</c>
/// environment variable, and with it unset — which is every build today —
/// <see cref="Start"/> returns null on its first line and nothing is
/// initialised, no client exists, and nothing is sent anywhere.
///
/// What makes this safe to switch on is the scrubbing rather than the sampling.
/// Metis holds provider API keys, photographs the screen, and keeps the text of
/// conversations. None of that may ever leave the machine in a crash report, and
/// the privacy policy names Sentry and promises exactly that. So the report is
/// built to carry an exception and the place it happened, and nothing else:
/// personal data collection is off, the Windows username is scrubbed out of
/// every path, and anything that looks like a key or a token is replaced before
/// the envelope is handed to the SDK.
/// </summary>
public static class CrashReporting
{
    /// <summary>The variable that switches this on. Absent on every shipped build.</summary>
    public const string DsnVariable = "METIS_SENTRY_DSN";

    /// <summary>Whether a DSN was found and the SDK actually started.</summary>
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// Starts crash reporting if a DSN is configured.
    ///
    /// Returns the SDK's disposable so the caller can shut it down on exit, or
    /// null when there is nothing to shut down. Never throws: a fault in crash
    /// reporting must not become the crash.
    /// </summary>
    public static IDisposable? Start(string appVersion)
    {
        var dsn = Environment.GetEnvironmentVariable(DsnVariable);
        if (string.IsNullOrWhiteSpace(dsn))
        {
            return null;
        }

        try
        {
            var handle = SentrySdk.Init(options =>
            {
                options.Dsn = dsn;
                options.Release = $"metis@{appVersion}";

                // The free tier is 5,000 events a month, which is a budget
                // rather than a bucket. Traces are worth almost nothing on a
                // desktop app and would spend it in a week.
                options.TracesSampleRate = 0;
                options.AutoSessionTracking = false;

                // Off, and then scrubbed anyway below. This alone would still
                // let a username through inside an exception message.
                options.SendDefaultPii = false;
                options.AttachStacktrace = true;

                // Breadcrumbs on this app would record what the user was doing,
                // which is the one thing that cannot be allowed to leave.
                options.MaxBreadcrumbs = 0;

                options.SetBeforeSend(static (SentryEvent evt, SentryHint _) => Scrub(evt));
            });

            IsEnabled = true;
            return handle;
        }
        catch
        {
            // A DSN that will not parse, no network, a version of the SDK that
            // disagrees — none of it is worth failing a launch over.
            IsEnabled = false;
            return null;
        }
    }

    /// <summary>
    /// Reports an exception that was handled, so the app keeps running.
    ///
    /// A no-op when reporting is off, which means call sites do not need to ask
    /// whether it is on.
    /// </summary>
    public static void Capture(Exception exception, string where)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            SentrySdk.CaptureException(exception, scope => scope.SetTag("where", where));
        }
        catch
        {
            // Reporting a crash must never cause one.
        }
    }

    /// <summary>
    /// Takes the things Metis holds that must not leave the machine back out of
    /// an event before it is sent.
    ///
    /// This runs on every event, including ones the SDK raised itself, because
    /// the leak that actually happens is not a deliberate field — it is a path
    /// with a person's name in it, or an exception message that stringified the
    /// request that failed along with its Authorization header.
    /// </summary>
    private static SentryEvent? Scrub(SentryEvent evt)
    {
        try
        {
            evt.User = new SentryUser();
            evt.ServerName = null;

            if (evt.Message?.Formatted is { Length: > 0 } formatted)
            {
                evt.Message.Formatted = Redact(formatted);
            }

            foreach (var exception in evt.SentryExceptions ?? [])
            {
                if (exception.Value is { Length: > 0 } value)
                {
                    exception.Value = Redact(value);
                }

                foreach (var frame in exception.Stacktrace?.Frames ?? new List<SentryStackFrame>())
                {
                    if (frame.FileName is { Length: > 0 } file)
                    {
                        frame.FileName = Redact(file);
                    }
                }
            }
        }
        catch
        {
            // If scrubbing itself fails, drop the event rather than send one
            // that may not have been cleaned.
            return null;
        }

        return evt;
    }

    /// <summary>
    /// Replaces the user's name and anything key-shaped in a string.
    ///
    /// The rule itself lives in <see cref="SecretRedaction"/> in Metis.Core,
    /// where it is covered by tests. This is the only thing standing between a
    /// stack frame full of build paths and a third party, so it is not a rule
    /// that should live untested inside a WPF executable.
    /// </summary>
    private static string Redact(string text) => SecretRedaction.Apply(text);

    /// <summary>
    /// A one-line summary for the diagnostics page, so a person can see whether
    /// anything is being sent without going looking for an environment variable.
    /// </summary>
    public static string Describe() =>
        IsEnabled
            ? "On. Crash reports are sent, with no screenshots, conversations or keys."
            : "Off. Nothing is sent anywhere.";
}
