import { APP_VERSION } from "./support";

/**
 * Crash reporting, which is off.
 *
 * There is no Sentry account behind this site today, so the whole thing is
 * conditional on `VITE_SENTRY_DSN` being set to something. With it unset — which
 * is every build right now, including production — `startErrorReporting` returns
 * on its first line: no network request, no console warning, no global handler,
 * and, because the SDK is behind a dynamic `import`, not even the bytes. A
 * visitor to a build with no DSN never downloads Sentry at all.
 *
 * When it is switched on it is configured for a free tier that allows 5,000
 * errors a month, which is a budget rather than a bucket. Hence a 5% trace
 * sample, no session replay, and a couple of filters for the noise every site on
 * the web collects from browser extensions.
 *
 * The scrubbing below is the part worth reading. Sentry is a third party this
 * site's privacy policy has to name, and the promise made there is specific:
 * no access token, no API key, no email address. `sendDefaultPii: false` covers
 * what the SDK collects deliberately; `redact` covers what leaks by accident,
 * which is the case that actually happens — a fetch that failed and stringified
 * its own `Authorization` header into the message, a validation error that
 * quoted the address somebody typed.
 */

const dsn = import.meta.env.VITE_SENTRY_DSN;

/** Whether this build can report anything. Used by the privacy policy's text. */
export const isErrorReportingConfigured = Boolean(dsn);

export function startErrorReporting(): void {
  if (!dsn) return;

  void import("@sentry/react")
    .then((Sentry) => {
      Sentry.init({
        dsn,
        release: `metis-website@${APP_VERSION}`,
        environment: import.meta.env.MODE,

        // Never let the SDK attach an IP address, a cookie, or a request body
        // on its own initiative. Everything Sentry knows about a person here is
        // something this file decided to send.
        sendDefaultPii: false,

        // 5% of page loads. Performance data is the cheapest thing to give up
        // when the month's quota is finite and errors are the point.
        tracesSampleRate: 0.05,

        integrations: [
          Sentry.browserTracingIntegration(),

          // Replaces the default breadcrumbs integration. Console breadcrumbs
          // are the widest leak in the SDK — anything any library ever logged,
          // verbatim — and the least useful of them for a bug report.
          Sentry.breadcrumbsIntegration({ console: false }),
        ],

        // Deliberately no `replayIntegration`. Session replay records the DOM,
        // which on this site means the sign-in form, and no amount of masking
        // makes that a thing to send somewhere on a $0 budget.

        ignoreErrors: [
          // Fired by layout engines, means nothing, arrives constantly.
          "ResizeObserver loop limit exceeded",
          "ResizeObserver loop completed with undelivered notifications",
          // A rejected promise carrying no Error has no stack worth the quota.
          /^Non-Error promise rejection captured/,
        ],

        denyUrls: [
          /^chrome-extension:\/\//i,
          /^moz-extension:\/\//i,
          /^safari-(web-)?extension:\/\//i,
        ],

        beforeSend: (event) => redact(event),
        beforeSendTransaction: (event) => redact(event),
        beforeBreadcrumb: (crumb) => redact(crumb),
      });
    })
    .catch(() => {
      // A crash reporter that crashes the page it is reporting on is worse than
      // no crash reporter. If the chunk will not load, this site simply has no
      // error reporting, which is the state it is in today anyway.
    });
}

// ------------------------------ Scrubbing ------------------------------

const REDACTED = "[redacted]";

/**
 * The shapes of the secrets this site can actually touch, matched on the value
 * rather than on the name of the field holding it — because the leak that
 * matters is the one where the value ended up somewhere nobody named.
 */
const secretPatterns: RegExp[] = [
  // "Authorization: Bearer …", however it got into a string.
  /\b[Bb]earer\s+[A-Za-z0-9._~+/=-]{8,}/g,
  // Any JWT, which on this site means a Supabase access or refresh token.
  /\beyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]+/g,
  // Supabase keys, publishable and — it should never be here, so all the more
  // reason to catch it — secret.
  /\bsb_(?:publishable|secret)_[A-Za-z0-9_-]{8,}/g,
  // OpenAI, Anthropic and OpenRouter provider keys, which a Max customer types
  // into the account page.
  /\bsk-[A-Za-z0-9_-]{12,}/g,
  // Google Gemini.
  /\bAIza[A-Za-z0-9_-]{20,}/g,
];

/** Anything carried in a query string or a URL fragment. */
const sensitiveParam =
  /\b(access[_-]?token|refresh[_-]?token|provider[_-]?token|id[_-]?token|api[_-]?key|apikey|password|secret|token|code)=([^&#\s"']+)/gi;

/**
 * Email addresses. Broad on purpose: over-redacting a support address in a
 * stack trace costs nothing, and under-redacting a customer's costs the promise
 * made in the privacy policy.
 */
const emailPattern = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g;

/**
 * Keys whose value goes wholesale, whatever it looks like. These are the
 * containers, not the contents — a headers object has no shape worth matching.
 */
const droppedKeys = new Set([
  "authorization",
  "cookie",
  "cookies",
  "headers",
  "password",
  "secret",
  "token",
  "access_token",
  "refresh_token",
  "api_key",
  "apikey",
  "email",
  "username",
  "ip_address",
]);

function redactString(value: string): string {
  let out = value;

  for (const pattern of secretPatterns) {
    out = out.replace(pattern, REDACTED);
  }

  out = out.replace(sensitiveParam, (_whole, name: string) => `${name}=${REDACTED}`);
  return out.replace(emailPattern, REDACTED);
}

/**
 * Walks an event and rewrites every string in it.
 *
 * Plain objects and arrays are rebuilt rather than mutated; anything else — a
 * class instance, a DOM node, a function that somehow ended up in there — is
 * returned untouched, because guessing at how to copy it is how a scrubber
 * becomes the thing that throws inside `beforeSend`. The depth limit is the
 * same idea: Sentry has already normalised the payload by this point, and a
 * structure deeper than this is not one worth following.
 */
function redact<T>(value: T, depth = 0): T {
  if (typeof value === "string") return redactString(value) as T;
  if (value === null || typeof value !== "object" || depth >= 8) return value;

  if (Array.isArray(value)) {
    return value.map((item: unknown) => redact(item, depth + 1)) as T;
  }

  const prototype = Object.getPrototypeOf(value) as object | null;
  if (prototype !== Object.prototype && prototype !== null) return value;

  const out: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
    out[key] = droppedKeys.has(key.toLowerCase()) ? REDACTED : redact(item, depth + 1);
  }

  return out as T;
}
