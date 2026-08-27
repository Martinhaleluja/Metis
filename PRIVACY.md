# Privacy

> **Draft — not yet reviewed by a lawyer.** This describes what the software
> actually does, which is the part worth getting right first. It is not a
> substitute for a privacy policy reviewed against the data protection law of
> the places Metis is distributed. Metis processes information belonging to
> people who never installed it — anyone whose message or document is on the
> screen — and that is the part a lawyer should look at.

Metis reads your screen to answer questions about it. This page says exactly
what that means: what is captured, when, where it goes, what is kept, and how to
get rid of it.

---

## The short version

- Metis captures your screen **only when you ask it something**. It does not
  watch between requests and it does not record.
- That capture is sent to **an AI provider you choose and pay for yourself**,
  using your own API key. Metis has no server in the middle.
- Content an application marks as private — banking apps, password managers,
  view-once photos in WhatsApp and Signal — is **blacked out before anything is
  sent**. Password boxes are never read.
- Your API keys live in **Windows Credential Manager**, never in a settings file
  or a log.
- Chats and memory are stored **encrypted on your own machine** and are readable
  only by your Windows account.
- You can delete all of it, at any time, from Settings.

---

## What is captured, and when

A capture happens when, and only when, you ask Metis a question with screen
context turned on. Nothing is captured while Metis is idle.

Each capture is:

- **A picture of your whole desktop**, across every monitor. For an ordinary
  question this is scaled down to at most 1280×720; when you point at one
  specific control it is kept larger, because the answer is a coordinate.
- **A list of on-screen controls** — their names, types and positions — read
  from Windows UI Automation, capped at 120 entries.
- **Your question**, typed or spoken. A spoken question is a short audio
  recording.
- **A little context**: the active window's title, the last few exchanges of the
  conversation, and a digest of what Metis has learned you are working on.

## What is never captured

- **Windows that mark themselves as protected.** Windows lets an application say
  "do not record this", and applications use it for exactly the things you would
  expect: banking apps, password managers, DRM video, and view-once media in
  WhatsApp and Signal. Metis finds those windows and paints them black before
  the image is encoded, so the pixels never exist in anything that gets
  uploaded. The model is told a region was withheld, so it says it cannot see
  rather than guessing.
- **Password fields.** The one you are typing into is blacked out. No password
  field's contents, name or identifier is ever read from the accessibility tree,
  in any mode.
- **Applications you exclude.** Settings has a list; anything matching by
  process name or window title is blacked out the same way.
- **Metis itself.** Metis's own windows are excluded from screen capture, so the
  conversation you are having is not inside the picture of it — and Metis does
  not appear in anyone else's screen recording either.

None of this is perfect. It depends on applications marking their own content
correctly and on Windows reporting it. If something on your screen must never
leave your machine, turn screen context off, or run Metis against a local model.

## Where it goes

To whichever AI provider you have configured, and nowhere else:

| Provider | What it receives |
|---|---|
| Google (Gemini) | The picture, the control list, your question, any voice recording |
| Anthropic (Claude) | The picture, the control list, your question |
| OpenAI | The picture, the control list, your question, any voice recording |
| OpenRouter | The above, routed to the model you pick |
| AssemblyAI | Voice recordings only, if you choose it for transcription |
| ElevenLabs | The text of Metis's reply only, if you choose it for speech |
| Ollama / OpenClaw | Nothing leaves your machine |

You are the customer of these providers, using your own key. What they do with
what you send is governed by **their** privacy policy and terms, not this one.
Read them. If you would rather nothing left the machine at all, point Metis at a
vision-capable local model through Ollama.

Metis's own backend is used for **sign-in only** — an email address and a
session token, held in Supabase. It never sees your screen, your questions or
your answers.

## What is kept, and where

Everything Metis keeps is on your own computer, under
`%LOCALAPPDATA%\Metis`:

| What | Where | Encrypted |
|---|---|---|
| Chat history | `chats\` | Yes, to your Windows account |
| What you are learning | `memory.json` | Yes, to your Windows account |
| Settings | `settings.json` | No — it holds no secrets |
| Diagnostic log | `logs\metis.log` | No — secrets are stripped from it |
| API keys | Windows Credential Manager | By Windows |

Screenshots are **not** stored. They exist in memory for the duration of the
request and are then gone.

The log records that a capture happened, how large it was, and how many regions
were withheld. It does not contain the picture.

## How to get rid of it

- **Settings → Privacy** deletes chats, memory and logs.
- Removing the folder `%LOCALAPPDATA%\Metis` removes everything Metis stores.
- Uninstalling Metis leaves that folder in place, so delete it too if you want
  nothing left behind.
- API keys are removed from Windows Credential Manager when you clear them in
  Setup.

Deleting your local records does not delete anything a provider kept. For that,
use that provider's own controls.

## The waitlist

The website collects an email address if you join the waitlist. It is stored in
Supabase and used to tell you when Metis is available. To be removed, email the
address below and it will be deleted.

## Autonomous agents

Metis can run a background task that operates your computer: creating and
editing files, running commands, and driving a browser. This is separate from
the teaching assistant, it only runs when you start it, and it works inside its
own folder unless you grant it access to one of yours.

An agent is refused access to credential stores outright, whatever permission it
has been given — SSH and cloud keys, browser profiles, saved-password stores,
`.env` files, and Metis's own records. It will not enter a password or complete
a human-verification check; at those pages it stops and hands the browser back
to you.

## Children

Metis is not intended for use by children.

## Changes

Material changes to this page will be noted in `CHANGELOG.md` and in the release
notes.

## Contact

https://github.com/Martinhaleluja/Metis/issues
