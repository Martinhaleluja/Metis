/// <reference types="vite/client" />

/**
 * The site's own version, injected by Vite from `package.json` at build time —
 * see the `define` block in `vite.config.ts`. It is written down in exactly one
 * place because a version that appears twice is a version that will one day
 * disagree with itself, and the one a customer quotes in a support email is
 * whichever copy you forgot to bump.
 */
declare const __APP_VERSION__: string;

/**
 * The build-time configuration this site reads. Declaring it means a typo in an
 * environment variable name is a compile error rather than a silent
 * `undefined` at three in the morning. Every one is optional: the site has to
 * render, and say what is missing, in a build where none of them are set.
 */
interface ImportMetaEnv {
  readonly VITE_SUPABASE_URL?: string;
  readonly VITE_SUPABASE_PUBLISHABLE_KEY?: string;
  readonly VITE_METIS_API_URL?: string;
  readonly VITE_SENTRY_DSN?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
