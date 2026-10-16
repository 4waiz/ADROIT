// SPDX-License-Identifier: LicenseRef-BRIDGE
// BRIDGE at adroit.bridgeae.com: the studio's page, with no simulation behind it.
//
// Cloudflare serves the production build of frontend/ as static assets. This Worker runs only
// for /api/* and /ws (wrangler.jsonc, assets.run_worker_first): on a GPU workstation those reach
// the Python service (bridge_studio) that owns the simulation, and nothing here reaches one. So
// every such request, read or write, is answered 503 with a body that says why, and the page,
// reading the same from /api/health, shows its offline state (frontend/src/components/
// OfflinePage.tsx) instead of waiting on a socket that cannot open.

interface Env {
  /** The page: frontend/ built into ./dist by `npm run build`. */
  ASSETS: { fetch(request: Request): Promise<Response> };
}

const OFFLINE = JSON.stringify({
  ok: false,
  live: false,
  product: "BRIDGE | Digital Twin Studio",
  // `detail` is what the page's REST helper shows for a failed request (frontend/src/api/rest.ts).
  detail:
    "The live simulation is offline. It runs on a GPU workstation with BRIDGE running locally " +
    "(scripts\\bridge.ps1 -Open); this site serves the studio's page only.",
});

function offline(): Response {
  return new Response(OFFLINE, {
    status: 503,
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
  });
}

export default {
  fetch(request: Request, env: Env): Response | Promise<Response> {
    const { pathname } = new URL(request.url);
    // The socket's upgrade is refused the same way: a 503 before any handshake.
    if (pathname === "/ws" || pathname === "/api" || pathname.startsWith("/api/")) return offline();
    return env.ASSETS.fetch(request);
  },
};
