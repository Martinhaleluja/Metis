# METIS
### An AI companion that teaches you to make media — then gets out of the way

**UNESCO Youth Hackathon 2026**
*Play Your Part: Youth Designing the Future of Media and Information Literacy*

---

## 1. Team members

| Name | Role | Country |
|---|---|---|
| **Martin Nakasole** | Developer — architecture, application, AI integration | Namibia |
| **Lamek Hidengwa** | Designer — interface, visual identity, user experience | Namibia |

Two members. Both aged 18–30. Project language: English.

**Repository:** `github.com/Martinhaleluja/Metis` — open source.

---

## 2. Problem statement

The most powerful media production tools in the world are now free. DaVinci
Resolve, used on feature films, costs nothing. So do Blender, Audacity, GIMP and
Canva's free tier.

**The software is free. The knowledge is not.**

A twenty-year-old in Windhoek can download a professional video editor this
afternoon. When it opens, he is looking at seven workspaces, several hundred
controls, and no vocabulary for any of them. He has something to say and no way
to say it.

The two things normally offered to him both fail:

- **Tutorials** are filmed on someone else's screen, in a different version of
  the software, at a video resolution his data bundle cannot afford. What he
  learns is hard to transfer to the interface actually in front of him.
- **AI that does the work for him** produces a finished artefact and leaves him
  exactly as capable as he was before. The output is not his, and tomorrow he
  needs the tool again.

One does not transfer. The other creates dependency. Neither produces a person
who can make media.

This is a media and information literacy problem. UNESCO's MIL framework spans
**access, evaluation and creation**. A young person who can only consume media
is an audience. A young person who can produce it participates in the
conversation. The barrier is not access to tools any more — it is the knowledge
to operate them, and that knowledge is distributed very unevenly.

---

## 3. Objectives

1. **Move learning into the moment of use.** Teach media production inside the
   software the learner is actually running, on their own project, rather than
   in a separate video.
2. **Build capability, not dependency.** Reduce guidance measurably as the
   learner demonstrates competence, so the tool is needed less over time.
3. **Work where bandwidth does not.** Run entirely offline on a modest personal
   laptop, with no data cost and no account.
4. **Lower the reading and language barrier** through voice input, spoken
   answers, and on-screen drawing instead of written instructions.
5. **Let communities extend it.** Allow any user to teach Metis about software
   it has never seen, in a plain text file, without programming.

---

## 4. Target audience

**Primary — young first-time media creators, 18–25, in low-bandwidth contexts.**
They have a personal or shared computer and free creative software, and no
access to structured training. They are the audience for whom the tool is free
and the tutorial is expensive.

**Secondary — educators and youth facilitators** teaching media production with
no specialist software training themselves, who need a classroom assistant that
explains as they go.

**Tertiary — community media, student journalists and civil-society
communicators** producing their own reporting and campaigns on free tools.

---

## 5. Alignment with the theme

Metis addresses the **creation** competency of media and information literacy,
which is the least-served of the three.

Media literacy is usually taught as reading: how to spot a fake, check a source,
identify sponsorship. That work matters and Metis supports it — because it sees
whatever is on the screen, a user can point at a byline, a badge or a label in
any application and ask what it is.

But literacy has always included writing. A generation that can evaluate media
without being able to produce it remains an audience. Metis exists so that a
young person with something to say can learn the tools of expression at the
moment they reach for them — and so that the ability to publish is not decided
by whether someone had access to training.

---

## 6. Prototype / concept

Metis is a working Windows desktop application, not a mock-up. It is installed
and running today.

**How it works.** Metis sits at the top edge of the screen. The user holds
`Ctrl+Alt` and speaks, or holds `Ctrl+Alt+Shift` and points at a specific
control and asks about it. Metis captures the desktop, reads the interface
through Windows' own accessibility layer, and answers about the application
actually in front of the user.

**How it teaches.** Metis draws directly over the screen: an outline that takes
the exact shape of the control it means, an arrow that draws itself toward a
target, numbered steps along a menu path, freehand strokes to explain visually.
Its companion detaches from the user's cursor, moves to the control like a
second hand on the screen, and then waits for the user to perform the step
themselves.

**Four operating modes**, chosen by the user at any time:

| Mode | Behaviour |
|---|---|
| **Learn** | The user does the work. Metis explains why, points at each control, and waits. |
| **Guide** | The user does the work. Metis directs them to the next step without theory. |
| **Assist** | Metis shares the work and leaves the meaningful choices to the user. |
| **Autopilot** | Metis performs the task in small verified steps and can explain them afterwards. |

The mode is **enforced outside the AI model**, in a separate policy layer. Learn
mode cannot click, regardless of what the model returns. This is a structural
guarantee rather than an instruction the model is asked to follow.

**Built and verified:** 347 automated tests pass. Self-contained installer,
per-user, no administrator rights, Windows 10 (1809) or later.

---

## 7. Creativity and originality

**Guidance that fades.** Metis keeps a per-application record of which steps the
user has completed unaided, and deliberately says less about skills they have
proven. First encounter: a full explanation. Later: a short cue. Once mastered:
silence. Almost every AI assistant is designed to be needed again tomorrow;
Metis is designed to be needed less. This is the core of the idea and, as far as
we know, is not how any comparable assistant behaves.

**Teaching by drawing and demonstration.** Instructions do not depend on naming
things the learner does not yet have words for. Metis marks the control and
moves to it.

**Skills anyone can write.** A skill is a plain markdown file describing a
program — where things live, what the jargon means, the order of a workflow.
Metis loads it when the application or the request matches. A teacher can write
one for the software their class uses; a community can share one. This is how
the system reaches software we have never seen, without us writing code for it.

**Honest refusal.** Metis will not act on a screen it has not confirmed reading,
and states when it cannot resolve what the user pointed at, rather than guessing.

---

## 8. Feasibility

The prototype is complete and working, which removes most delivery risk.

- Built in C# on .NET 8 with WPF; screen capture, UI Automation, overlay
  rendering and voice are all implemented and tested.
- Model-agnostic: Google Gemini, OpenAI, Anthropic, or a local model through
  Ollama. Switching provider is a settings change, so the project is not
  dependent on any single vendor's pricing or availability.
- Distributed as a signed-installer-ready, self-contained executable requiring
  no runtime installation on the target machine.
- Open source, so the work survives the team.

**Known engineering risks and mitigations:** guidance quality depends on the
chosen model, and the small local model is measurably weaker than cloud models —
mitigated by supporting both and defaulting to a free cloud tier where a
connection exists. Coordinate accuracy on unusual interfaces is mitigated by
resolving targets through the accessibility tree rather than relying on the
model's estimate.

---

## 9. Sustainability

**Financial.** Two tiers. A free tier where the user supplies their own model
key or runs entirely offline — this costs us nothing per user, so it can remain
free permanently rather than until funding runs out. A paid tier (N$99/month,
roughly US$5) provides hosted access with no setup, and funds development.
Education and community-organisation licences are free.

**Technical.** Open source under a permissive licence, so schools and NGOs can
run and adapt it without depending on us. The offline path means a deployment
has no recurring cost once installed.

**Community.** The skills library is the mechanism for growth: every teacher or
creator who writes a skill for their software makes Metis useful to everyone
else using it. The system improves through use rather than only through our
development time.

---

## 10. Social impact and inclusion

- **Fully offline capability.** With Ollama, Whisper.cpp and Piper installed,
  nothing leaves the machine and no connection is required. Bandwidth is a
  significant barrier in our region and this removes it.
- **Voice in and voice out**, lowering the typing and reading barrier.
- **Visual instruction** that does not require the learner to already know the
  vocabulary.
- **Runs without administrator rights**, per-user, on shared and managed
  computers — the machines young people in our context actually have access to.
- **Free tools all the way down.** The creative software is free; Metis's free
  tier is free; the offline path has no running cost.

**Safety and transparency.** Screen capture is disclosed before installation and
again during onboarding, and can be switched off at any time. In every mode
including Autopilot, Metis stops before anything that deletes, purchases, sends,
submits or touches credentials — enforced in code and not configurable. API keys
are stored in Windows Credential Manager, never in a settings file. `F12` stops
everything immediately.

---

## 11. Honest limitations

Stated deliberately, because they are real and a jury would find them anyway.

- **English only.** No localisation. For a UNESCO context this is the single
  largest gap and our highest-priority next step.
- **Windows only.** macOS requires rewriting the capture, automation and overlay
  layers, not porting them.
- **Accessibility is incomplete.** Light and dark themes ship and follow the
  system setting, but a high-contrast theme is not implemented and screen-reader
  labelling has not been audited.
- **The offline path asks a lot of the user**, who must install three runtimes
  themselves — a real barrier for exactly the low-resource users it is meant to
  serve. Packaging this into a single install is planned.

---

## 12. Next twelve months

1. **Localisation**, beginning with Portuguese, French, Swahili and Afrikaans.
2. **One-click offline install** bundling the local speech and language models.
3. **Accessibility audit**: high-contrast theme and screen-reader labelling.
4. **A public skills library** with a first set covering DaVinci Resolve,
   Blender, Audacity and GIMP, written with educators.
5. **Classroom pilot** with a Namibian secondary school or youth media
   organisation, measuring whether guidance actually fades — the objective the
   whole design rests on.

---

## 13. Running the prototype

`Metis-Setup-1.0.0-win-x64.exe` — self-contained, no .NET runtime required,
per-user installation, Windows 10 1809 or later, x64.

On first launch a wizard covers the model provider choice, the screen-capture
disclosure, the operating mode, the keyboard shortcuts, voice, and appearance.

Source code, issues and build instructions: `github.com/Martinhaleluja/Metis`
