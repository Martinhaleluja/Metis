const url = import.meta.env.VITE_SUPABASE_URL as string | undefined;
const publishableKey = import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY as string | undefined;

/** False when the site is built without credentials, so the form can say so
 *  instead of failing silently against a client that cannot reach anything. */
export const isSupabaseConfigured = Boolean(url && publishableKey);

/**
 * Calls one of the waitlist functions over Supabase's REST interface.
 *
 * The supabase-js client also carries realtime, auth, storage and edge
 * functions, which is most of a megabyte of JavaScript for a page that makes
 * two POST requests. This talks to the same PostgREST endpoint the client
 * would, with the publishable key that is meant to ship in a browser.
 */
export async function rpc<T>(fn: string, args: Record<string, unknown> = {}): Promise<T> {
  if (!url || !publishableKey) throw new Error("Supabase is not configured");

  const response = await fetch(`${url}/rest/v1/rpc/${fn}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      apikey: publishableKey,
      Authorization: `Bearer ${publishableKey}`,
    },
    body: JSON.stringify(args),
  });

  if (!response.ok) {
    throw new Error(`${fn} responded ${response.status}`);
  }

  return (await response.json()) as T;
}
