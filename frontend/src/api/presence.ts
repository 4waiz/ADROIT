// SPDX-License-Identifier: LicenseRef-BRIDGE
// Whether a simulation answers this page. On the machine that runs it the service serves the
// page itself, and the socket's reconnect loop is all there is. Served from anywhere else, as at
// adroit.bridgeae.com where Cloudflare serves the page with no simulation behind it, the page
// asks /api/health first and, while nothing answers, shows its offline state instead of a
// spinner that would never end (components/OfflinePage.tsx).

/** "checking" until the first answer, then "live" while a service answers, "offline" while not. */
export type Presence = "checking" | "live" | "offline";

/** True when the page comes from this machine (localhost, 127.0.0.1, [::1], *.localhost). */
export function servedLocally(hostname: string = typeof location === "undefined" ? "localhost" : location.hostname): boolean {
  const host = hostname.toLowerCase().replace(/\.$/, "");
  return host === "localhost" || host === "127.0.0.1" || host === "[::1]" || host === "::1" || host.endsWith(".localhost");
}

/** Does a BRIDGE service answer here? A 503 (no simulation behind this host), a page that is not
 *  the service's JSON, a network error and a timeout all mean no. */
export async function serviceAnswers(timeoutMs = 6000): Promise<boolean> {
  try {
    const response = await fetch("/api/health", {
      cache: "no-store",
      headers: { Accept: "application/json" },
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (!response.ok) return false;
    const body = (await response.json()) as { ok?: unknown } | null;
    return body?.ok === true;
  } catch {
    return false;
  }
}
