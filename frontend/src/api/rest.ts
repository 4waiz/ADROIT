// SPDX-License-Identifier: LicenseRef-BRIDGE
// REST helpers. Same origin: the service serves the page, and the dev server proxies /api.

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function readError(response: Response): Promise<string> {
  try {
    const body = await response.json();
    if (typeof body?.detail === "string") return body.detail;
    if (Array.isArray(body?.detail)) return body.detail.map((d: { msg?: string }) => d.msg).join("; ");
    return JSON.stringify(body);
  } catch {
    return response.statusText || `HTTP ${response.status}`;
  }
}

export async function getJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { ...init, headers: { Accept: "application/json", ...(init?.headers ?? {}) } });
  if (!response.ok) throw new ApiError(response.status, await readError(response));
  return (await response.json()) as T;
}

export async function sendJson<T>(path: string, method: "POST" | "PUT" | "DELETE", body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new ApiError(response.status, await readError(response));
  return (await response.json()) as T;
}

export async function getBinary(path: string): Promise<ArrayBuffer> {
  // Always checked with the service: the geometry's gen=1 after a restart can name other meshes
  // than a copy this browser kept from before it (a 304 when it is the same).
  const response = await fetch(path, { cache: "no-cache" });
  if (!response.ok) throw new ApiError(response.status, await readError(response));
  return await response.arrayBuffer();
}
