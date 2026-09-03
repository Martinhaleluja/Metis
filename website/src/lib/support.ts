/**
 * Where a person writes when something is wrong, and what goes with it.
 *
 * A support email that says "it doesn't work" costs two round trips before
 * anyone can begin: which browser, which build, which page. So the link builds
 * the message with those already in it, and says out loud that they are there —
 * a diagnostic block a person cannot see is a diagnostic block they cannot
 * decide to delete.
 *
 * The rule about what may go in it is short and absolute: nothing secret. Not
 * the Supabase access token, not a provider key, not anything read out of
 * storage. The account id is a bare identifier that grants nothing on its own,
 * and it goes in only when somebody is actually signed in — a line reading
 * "Account: null" is worse than no line, because it looks like a fault in the
 * thing they are already writing to complain about.
 */

/** The one place this address is written down. */
export const SUPPORT_EMAIL = "support@metis.software";

/** The site's version, from `package.json`. See `env.d.ts`. */
export const APP_VERSION = __APP_VERSION__;

const subject = "Metis support request";

/**
 * A `mailto:` for the support address, with the diagnostics already filled in.
 *
 * `accountId` is the signed-in customer's Supabase user id, or nothing at all.
 * Callers pass it from `useAuth()`; there is deliberately no way to reach into
 * a session from in here, so there is no route by which a token could end up in
 * the body by accident.
 */
export function supportMailto(accountId?: string): string {
  const body = [
    "Tell us what happened, and what you expected instead:",
    "",
    "",
    "What you were doing at the time:",
    "",
    "",
    "--- Please keep the lines below. They tell us where to look. ---",
    "",
    ...diagnostics(accountId),
  ].join("\n");

  return `mailto:${SUPPORT_EMAIL}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
}

function diagnostics(accountId?: string): string[] {
  const lines = [
    // The path, not `location.href`. A full URL can carry a query string or a
    // fragment, and a fragment is exactly where an auth link puts a token —
    // copying one of those into an email would defeat the point of the rule
    // above. The path is the part that says which page broke.
    `Page: ${safe(() => window.location.pathname)}`,
    `Version: ${APP_VERSION}`,
    `Browser: ${safe(() => navigator.userAgent)}`,
    `System: ${safe(platformName)}`,
    `Screen: ${safe(() => `${window.screen.width}x${window.screen.height} at ${window.devicePixelRatio}x`)}`,
    `Language: ${safe(() => navigator.language)}`,
  ];

  if (accountId) lines.push(`Account: ${accountId}`);

  return lines;
}

/**
 * The operating system, from whichever of the two answers this browser gives.
 *
 * `userAgentData` is the modern one and is Chromium-only; `navigator.platform`
 * is deprecated, universally supported, and still the only answer Firefox and
 * Safari have. Neither is worth a hard failure, so an unknown is a word rather
 * than an exception.
 */
function platformName(): string {
  const modern = (navigator as Navigator & { userAgentData?: { platform?: string } })
    .userAgentData;

  return modern?.platform || navigator.platform || "unknown";
}

/**
 * A support link that throws while being built is a support link nobody can
 * use to report the thing that threw.
 */
function safe(read: () => string): string {
  try {
    return read();
  } catch {
    return "unknown";
  }
}
