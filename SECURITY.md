# Security policy

Metis reads the screen of the computer it runs on and can run tasks on it, so a
security problem in Metis is a security problem for whoever installed it. If you
find one, please tell us before you tell anyone else.

## Reporting a vulnerability

Open a **private** report through GitHub:

1. Go to https://github.com/Martinhaleluja/Metis/security/advisories/new
2. Describe what you found and how to reproduce it.

If that is not available to you, open an ordinary issue saying only that you
have a security report and would like a private channel — **do not put the
details in a public issue.**

Please include, if you can: what version you are on, what Windows build, what
you did, what happened, and what you think an attacker could do with it.

## What to expect

- An acknowledgement within **5 working days**.
- An assessment, and if it is a real issue, an estimated fix date, within
  **10 working days**.
- Credit in the release notes when the fix ships, unless you would rather not be
  named.

Metis is a small project with no security team and no bug bounty. What is
offered here is an honest answer and a fix, not a payment.

## What is in scope

Anything that gets Metis to:

- capture or transmit content it was told not to — a window marked private, a
  password field, an excluded application;
- read a credential, or persuade an agent to;
- run code the user did not ask for, including through the update mechanism;
- write a secret into the log, settings, or any other unencrypted file;
- send data anywhere other than the provider the user configured.

Prompt injection from screen or web content is in scope where it causes any of
the above. Metis treats everything it reads as content rather than instruction,
and a case where that fails is worth reporting.

## What is out of scope

- The AI providers themselves. What Google, Anthropic or OpenAI do with what you
  send them is between you and them.
- A model giving wrong or bad advice. Metis can be wrong; that is a quality
  problem, not a vulnerability.
- Anything that needs administrator access to the machine, or physical access to
  an unlocked session, since either already defeats any protection Metis has.
- Findings from automated scanners with no demonstrated impact.

## Known limitations

Stated plainly, because a security policy that only lists strengths is not
useful:

- **The installer is not code-signed.** Windows SmartScreen will warn about it.
  Verify the SHA-256 published in the release notes against the file you
  downloaded.
- **Redaction depends on applications marking their own content.** Metis honours
  the Windows flag for "do not capture", but an application that does not set it
  will be captured. If something must never leave your machine, turn screen
  context off or use a local model.
- **Stored records are encrypted to your Windows account**, which protects a
  stolen disk and other user accounts. It does not protect against malware
  already running as you.
