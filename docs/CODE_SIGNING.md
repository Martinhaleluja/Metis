# Code signing, and why Metis is not signed yet

Every person who downloads Metis today meets this:

> **Windows protected your PC.** Microsoft Defender SmartScreen prevented an
> unrecognised app from starting. Publisher: Unknown publisher.

The "Run anyway" button is hidden behind **More info**. For an application whose
pitch is *"let me look at your screen"*, that is the worst possible first
impression, and it is almost certainly the single largest source of lost users —
larger than anything marketing can recover.

This file says what it would take to remove it, and is honest that **there is no
free way to do it for Metis today**.

---

## Why the usual free answers do not apply

Both of the well-known free programmes require the software to be open source.
**Metis is source-available under a proprietary EULA** — the code is public so
people can read what an app that photographs their screen actually does, but all
rights are reserved. That is a deliberate decision, and it disqualifies Metis
from both.

| Option | Cost | Status for Metis |
| --- | --- | --- |
| **SignPath Foundation** | Free | ❌ **Not eligible.** Requires an OSI-approved licence with no commercial dual-licensing, and "may not contain any proprietary, non open-source component". Metis is proprietary. |
| **Certum Open Source** | ~€30 | ❌ **Not eligible.** Same requirement. |
| **Azure Artifact Signing** (was Trusted Signing) | $9.99/mo | ❌ **Not available in Namibia.** The individual tier is limited to the **United States and Canada**; the organisation tier additionally wants three years of verifiable business history. |
| **Microsoft Store** | Free | ⚠️ **Worth investigating.** Store apps are signed and trusted automatically. But Metis installs a system-wide keyboard hook and captures the screen, which sits awkwardly with Store policy. Treat as a possible second channel, not a replacement. |
| **Commercial OV certificate** | ~$200–400/yr | ✅ Works, costs money, and needs a registered business. |
| **Commercial EV certificate** | ~$400–700/yr | ✅ Works, immediate SmartScreen reputation, hardware token, registered business. |

> Sources: SignPath Foundation terms; Microsoft Learn, *Code signing options for
> Windows app developers*; Azure Artifact Signing pricing. Checked 3 September
> 2026. **All of these change — re-check before acting**, particularly Azure's
> country list, which has been expanding.

### The realistic sequence

1. **Now, at N$0** — do the free mitigations in the next section.
2. **When the Close Corporation exists** — that unlocks the organisation route
   for a commercial OV certificate, and possibly Azure if the country list has
   grown by then. This is a second, quieter reason to register the company.
3. **Then** — roughly $200/year, or $10/month, permanently. Put it in the
   budget and stop thinking about it.

---

## What to do while it is unsigned

These cost nothing and materially reduce the damage.

### 1. Predict the warning before the user meets it

A warning you predicted reads as honesty. A warning you did not reads as malware.
Put this on the download page, above the button:

> **Windows will warn you about this download.** Metis is not code-signed yet —
> a certificate needs a registered company, which is coming. Windows shows the
> same warning for every new application from a small developer.
>
> To check you have the real file, compare its SHA-256 against the one published
> on the release page:
>
> ```powershell
> Get-FileHash .\Metis-Setup-3.15.0-win-x64.exe -Algorithm SHA256
> ```

### 2. Publish the SHA-256 on every release — this is currently missing

The updater is built to verify the download against a checksum in the release
notes, and `README.md` tells users to check it. **Release v3.15.0 publishes no
checksum**, so that verification is being skipped.

`installer/build-installer.ps1` prints the hash when it finishes. Paste it into
the release body in a form the updater's regex can find:

```
SHA-256: 17542086b54edfd0bc350baa226d761265047a51a822da7fdea79d5508745d64
```

GitHub also exposes the digest itself on the release asset
(`assets[].digest`), and `UpdateService` now uses that as a fallback — so a
release where somebody forgot to paste the hash is still verified. Paste it
anyway: the human-readable line is what a cautious user checks by hand.

### 3. Do not obscure it

Never tell users to disable SmartScreen or add an antivirus exclusion. It trains
exactly the habit that gets people compromised, and it would be a bad look on a
product that asks to see their screen.

---

## Signing, once a certificate exists

`installer/build-installer.ps1` produces `installer\output\Metis-Setup-<version>-win-x64.exe`.
Two things must be signed: the application executable **before** it is packaged,
and the installer **after** Inno Setup produces it. Signing only the installer
leaves an unsigned binary on disk that some security tools flag later.

```powershell
$sign = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
$ts   = "http://timestamp.digicert.com"

# The app, before packaging
& $sign sign /fd SHA256 /td SHA256 /tr $ts /a `
    "artifacts\installer-publish\Metis.exe"

# The installer, after
& $sign sign /fd SHA256 /td SHA256 /tr $ts /a `
    "installer\output\Metis-Setup-3.16.0-win-x64.exe"

# Always verify rather than trusting the exit code
& $sign verify /pa /v "installer\output\Metis-Setup-3.16.0-win-x64.exe"
```

**Always timestamp** (`/tr`). Without it every signature expires with the
certificate and previously-shipped installers start warning again. With it they
stay valid after the certificate lapses.

Inno Setup can sign automatically via `SignTool=` in `installer/Metis.iss`, which
is tidier than a separate step once a certificate is in place.

### Reputation still takes time

An OV certificate does not clear SmartScreen instantly. Reputation accrues with
downloads, and there is a quiet period after the first signed release where the
warning persists. An EV certificate skips that queue, which is most of what the
extra cost buys. Sign early so the clock starts.

---

## The other half of trust

Signing removes one warning. It does not answer *"why should I let this look at
my screen?"* — and Metis already answers that better than most funded products:
it looks only when asked, blacks out windows you mark private, never reads
password fields, encrypts conversations locally, and can run entirely offline
against a model on your own machine.

That story is worth more than a certificate, and unlike a certificate it costs
nothing. Lead with it. The certificate removes an objection; the privacy design
is the reason someone installs it anyway.
