using System.Text;

namespace Metis.AI;

/// <summary>
/// Turns the raw fragments of a reply arriving over the wire into the sentence
/// Metis is going to say, published as it is written.
///
/// The reply is one JSON object matching the plan schema, so there is no
/// separate "text" field to read: the sentence is a string value part-way
/// through an object that is still being generated. What makes that workable is
/// that the value only ever grows, so re-reading the buffer after every
/// fragment yields a longer prefix of the same sentence and never a different
/// one — which means the difference can be appended straight to the screen.
///
/// Nothing is published until the sentence actually starts, so the fields the
/// schema puts before it cost the reader nothing.
/// </summary>
internal sealed class StreamingPlanText
{
    private readonly StringBuilder _raw = new();
    private readonly IProgress<string>? _sink;
    private int _published;
    private bool _finished;

    internal StreamingPlanText(IProgress<string>? sink) => _sink = sink;

    /// <summary>Everything received so far, for the ordinary parser to read.</summary>
    internal string Raw => _raw.ToString();

    /// <summary>Whether any of the answer has already been shown to the user.</summary>
    internal bool HasPublished => _published > 0;

    internal void Append(string? fragment)
    {
        if (string.IsNullOrEmpty(fragment))
        {
            return;
        }

        _raw.Append(fragment);

        // Once the sentence has been closed off, later fields in the object
        // cannot change it, so there is nothing left to re-read.
        if (_sink is null || _finished)
        {
            return;
        }

        if (!AssistantPlanParser.TryReadSpokenTextPrefix(_raw.ToString(), out var spoken, out var complete))
        {
            return;
        }

        _finished = complete;
        if (spoken.Length <= _published)
        {
            return;
        }

        _sink.Report(spoken[_published..]);
        _published = spoken.Length;
    }
}
