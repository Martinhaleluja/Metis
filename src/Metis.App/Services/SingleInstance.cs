using System.Threading;

namespace Metis.App.Services;

/// <summary>
/// Lets a second copy of Metis hand its launch over to the copy already
/// running, instead of telling the user off and quitting.
///
/// The old behaviour was a mutex, a message box, and nothing else. That was
/// wrong twice over. It was wrong for the ordinary case, because clicking the
/// Start-menu shortcut while Metis sits in the tray is a request to *see*
/// Metis, and the honest answer to it is to show the notch — not to explain
/// that the program the user is trying to open is already open. And it was
/// wrong for the broken case, because a copy whose window failed to build
/// keeps the mutex forever while showing nothing at all, so every launch from
/// then on hits the same dead end with no way out short of Task Manager.
///
/// So the mutex answers "is someone else here", and a pair of named events
/// answer the question that actually matters: "and are they alive?". The
/// newcomer knocks, waits briefly for an answer, and if none comes it treats
/// the holder as dead and starts up anyway. A user is far better served by a
/// second instance than by a program that cannot be opened.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Metis.Desktop.Companion";
    private const string KnockName = @"Local\Metis.Desktop.Activate";
    private const string AnswerName = @"Local\Metis.Desktop.Activated";

    /// <summary>
    /// How long a newcomer waits for the running copy to acknowledge before
    /// deciding it is not really there.
    ///
    /// Generous rather than snappy: the running copy may be mid-startup, or
    /// paging back in after the machine has been asleep, and mistaking a slow
    /// instance for a dead one produces the duplicate this class exists to
    /// prevent. Three seconds is longer than a healthy answer ever takes and
    /// short enough that a genuinely dead holder is not an ordeal.
    /// </summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(3);

    private readonly Mutex _mutex;
    private readonly bool _owned;
    private EventWaitHandle? _knock;
    private EventWaitHandle? _answer;
    private Thread? _listener;
    private volatile bool _stopping;

    private SingleInstance(Mutex mutex, bool owned)
    {
        _mutex = mutex;
        _owned = owned;
    }

    /// <summary>Whether this process should go on to build its UI.</summary>
    public bool ShouldStart { get; private init; }

    /// <summary>
    /// True when we started despite another process holding the mutex, because
    /// it never answered. Worth logging: it means a previous Metis is stuck.
    /// </summary>
    public bool TookOverFromUnresponsive { get; private init; }

    /// <summary>
    /// Claims the instance, or hands off to the copy already running.
    ///
    /// Returns an object whose <see cref="ShouldStart"/> says what to do. When
    /// it is false the handover succeeded and this process should exit quietly
    /// — quietly being the point, since the user is about to watch the notch
    /// they asked for slide down from the copy that was already there.
    /// </summary>
    public static SingleInstance Claim()
    {
        var mutex = new Mutex(true, MutexName, out var isFirst);

        if (isFirst)
        {
            return new SingleInstance(mutex, owned: true) { ShouldStart = true };
        }

        // Someone holds the mutex. Knock, and see whether anyone is home.
        if (Knock())
        {
            mutex.Dispose();
            return new SingleInstance(mutex, owned: false) { ShouldStart = false };
        }

        // No answer. The holder is a process that is running but cannot show
        // itself, which is indistinguishable to the user from Metis being
        // broken. Start anyway, without the mutex.
        return new SingleInstance(mutex, owned: false)
        {
            ShouldStart = true,
            TookOverFromUnresponsive = true
        };
    }

    private static bool Knock()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(KnockName, out var knock) ||
                !EventWaitHandle.TryOpenExisting(AnswerName, out var answer))
            {
                return false;
            }

            using (knock)
            using (answer)
            {
                // Clear any answer left over from an earlier exchange, so a
                // stale signal cannot be mistaken for a reply to this knock.
                answer.Reset();
                knock.Set();
                return answer.WaitOne(AnswerTimeout);
            }
        }
        catch
        {
            // Any failure to open or signal the pair means there is nothing on
            // the other end that this build understands. Treat it as silence.
            return false;
        }
    }

    /// <summary>
    /// Starts answering knocks from later launches.
    ///
    /// <paramref name="activate"/> is raised on a background thread; it is the
    /// caller's job to get to the UI thread. The answer is set before the
    /// callback runs rather than after, so a slow window animation cannot make
    /// the newcomer give up and start a duplicate half way through being
    /// served.
    /// </summary>
    public void ListenForOtherLaunches(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);

        if (!ShouldStart)
        {
            return;
        }

        _knock = new EventWaitHandle(false, EventResetMode.AutoReset, KnockName);
        _answer = new EventWaitHandle(false, EventResetMode.ManualReset, AnswerName);

        _listener = new Thread(() =>
        {
            while (!_stopping)
            {
                try
                {
                    if (!_knock.WaitOne(TimeSpan.FromSeconds(1)))
                    {
                        continue;
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    _answer.Set();
                    activate();
                }
                catch
                {
                    // A failure here must never take the process down: this
                    // thread exists to make Metis reachable, and a crash in it
                    // would recreate the very dead-end it was written to fix.
                }
            }
        })
        {
            IsBackground = true,
            Name = "Metis single-instance listener"
        };

        _listener.Start();
    }

    public void Dispose()
    {
        _stopping = true;

        // Wake the listener so it notices, rather than waiting out its poll.
        try
        {
            _knock?.Set();
        }
        catch
        {
            // Already disposed or never created.
        }

        _listener?.Join(TimeSpan.FromSeconds(1));
        _knock?.Dispose();
        _answer?.Dispose();

        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Not held, or held by another thread. Disposing is enough.
            }
        }

        _mutex.Dispose();
    }
}
