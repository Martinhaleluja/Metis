# Metis website

The public launch site: what Metis is, and the waitlist people join to get it.

Built with Vite, React and Tailwind v4. It is a static bundle with no server of
its own; the waitlist talks straight to Supabase from the browser.

## Running it

```bash
npm install
cp .env.example .env.local   # then fill in the two values
npm run dev
```

| Script | What it does |
| --- | --- |
| `npm run dev` | Dev server on port 5173 |
| `npm run build` | Typecheck, then build to `dist/` |
| `npm run preview` | Serve the built `dist/` |
| `npm run icons` | Regenerate `src/lib/appIcons.ts` from simple-icons |

## Environment

Both values belong in `.env.local`, which is not committed.

| Variable | Value |
| --- | --- |
| `VITE_SUPABASE_URL` | The Supabase project URL |
| `VITE_SUPABASE_PUBLISHABLE_KEY` | The publishable (`sb_publishable_…`) key |

Neither is a secret. The publishable key is meant to ship in a browser bundle,
and the schema below is built so that it cannot do anything beyond joining the
waitlist and reading a count. If the variables are missing the site still
renders and the form says the waitlist is not connected, rather than failing
silently.

## How the waitlist is stored

Everything lives in the same Supabase project as the rest of the Metis backend,
in `public.waitlist_signups`.

That table has row level security enabled **and no policies at all**. That is
deliberate rather than an oversight: with no policy, the publishable key cannot
read or write a single row directly, so no visitor can enumerate the email
addresses of everyone else on the list. The database linter reports the missing
policies as an informational finding.

All access goes through two `security definer` functions, which are the only
things granted to `anon`:

- **`join_waitlist(p_email, p_referral_code, p_source)`** validates the address,
  inserts, and returns *only that person's own* position and referral code. It
  is idempotent, so joining twice returns the original place in the queue
  instead of an error, and it throttles to five signups per hour from one
  address hash.
- **`waitlist_count()`** returns the total as a single number and nothing else.

The count is polled rather than subscribed to, because a table with no read
policy cannot be broadcast over realtime to an anonymous visitor.

## Design notes

The palette, typography and motion follow the Apple design language: one white
theme with no dark variant, size-specific tracking that tightens as type grows,
translucent chrome that content scrolls underneath, and critically damped
springs with overshoot reserved for motion that follows a real gesture.

Everything that moves is behind `prefers-reduced-motion`, and the translucent
surfaces have `prefers-reduced-transparency` and `prefers-contrast` fallbacks.

The brand mark in `public/` is the icon shipped with the Windows application,
extracted from `installer/.generated/Metis.ico`, rather than a redrawing of it.

The hero clip is a real recording of Metis annotating a video editor. The
source was 1920x1080 at 60fps and 4.7MB; what ships is 1280x720 at 30fps with
the silent audio track stripped, which is 0.89MB. It loads behind its poster
at `preload="none"` and only starts fetching after the window load event, so
the largest paint is the 59kB poster rather than the video. It can be paused,
because anything moving on its own for more than five seconds needs a control
to stop it, and it does not autoplay at all under `prefers-reduced-motion`.
