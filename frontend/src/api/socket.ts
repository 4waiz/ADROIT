// SPDX-License-Identifier: Apache-2.0
// One WebSocket to the service: frames and messages in, commands and cursor samples out.
//
// Commands are never queued for later and never re-sent: a command issued while the socket is
// down is refused on the spot, so a reconnect cannot make a robot repeat a move. Each command
// carries a fresh id; the host answers it (and answers a repeated id with its first outcome).

import { live } from "../state/live";
import { useStore } from "../state/store";
import { telemetry } from "../state/telemetry";
import { decodeFrame } from "./frames";
import { getBinary, getJson } from "./rest";
import type { Ack, CommandRecord, EventRecord, HostStatus, Manifest, ServiceInfo } from "./types";

type AckWaiter = { resolve: (ack: Ack) => void; terminalOnly: boolean };

const TERMINAL = new Set(["completed", "rejected", "failed", "interrupted"]);

function uuid(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) return crypto.randomUUID();
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

class BridgeSocket {
  private ws: WebSocket | null = null;
  private retry = 0;
  private waiters = new Map<string, AckWaiter>();
  private loadingGeneration = -1;
  private lastTelemetryAt = 0;
  private pingTimer = 0;
  private closedByUs = false;

  connect(): void {
    this.closedByUs = false;
    const protocol = location.protocol === "https:" ? "wss" : "ws";
    const ws = new WebSocket(`${protocol}://${location.host}/ws`);
    ws.binaryType = "arraybuffer";
    this.ws = ws;
    useStore.setState({ connection: this.retry ? "reconnecting" : "connecting" });

    ws.onopen = () => {
      if (this.ws !== ws) return;
      this.retry = 0;
      useStore.setState({ connection: "open" });
      void this.refreshInfo();
      this.pingTimer = window.setInterval(() => this.ping(), 2000);
    };
    ws.onmessage = (event) => {
      if (this.ws !== ws) return;
      if (event.data instanceof ArrayBuffer) this.onFrame(event.data);
      else this.onMessage(String(event.data));
    };
    ws.onclose = () => {
      if (this.ws !== ws) return; // a socket this client already replaced
      window.clearInterval(this.pingTimer);
      this.ws = null;
      // Anything still waiting will never be answered on this connection: say so.
      for (const [id, waiter] of this.waiters) {
        waiter.resolve({ id, name: "", status: "failed", message: "connection lost before the service answered", result: {} });
      }
      this.waiters.clear();
      useStore.setState({ connection: this.closedByUs ? "closed" : "reconnecting", latencyMs: null, pending: {} });
      if (!this.closedByUs) {
        const delay = Math.min(8000, 400 * 2 ** this.retry++);
        window.setTimeout(() => this.connect(), delay);
      }
    };
    ws.onerror = () => {
      /* onclose follows */
    };
  }

  close(): void {
    this.closedByUs = true;
    const ws = this.ws;
    this.ws = null;
    window.clearInterval(this.pingTimer);
    ws?.close();
    useStore.setState({ connection: "closed" });
  }

  get isOpen(): boolean {
    return this.ws?.readyState === WebSocket.OPEN;
  }

  /** Send a command; resolves with the host's answer (the terminal one unless `accepted` is enough). */
  command(name: string, args: Record<string, unknown> = {}, opts: { terminal?: boolean } = {}): Promise<Ack> {
    const id = uuid();
    if (!this.isOpen) {
      const ack: Ack = { id, name, status: "rejected", message: "not connected to the simulation service; nothing was sent", result: {} };
      useStore.getState().toast("err", name, ack.message);
      return Promise.resolve(ack);
    }
    const store = useStore.getState();
    useStore.setState({ pending: { ...store.pending, [id]: { id, name, args, sentAt: performance.now() } } });
    return new Promise<Ack>((resolve) => {
      this.waiters.set(id, { resolve, terminalOnly: opts.terminal ?? false });
      this.ws!.send(JSON.stringify({ type: "command", id, name, args }));
    });
  }

  cursor(arm: string, position: number[], quat: number[], phase: "start" | "update" | "end"): void {
    if (!this.isOpen) return;
    this.ws!.send(JSON.stringify({ type: "cursor", arm, position, quat, phase }));
  }

  private ping(): void {
    if (this.isOpen) this.ws!.send(JSON.stringify({ type: "ping", t: performance.now() }));
  }

  private onFrame(data: ArrayBuffer): void {
    const frame = decodeFrame(data);
    if (!frame) return;
    const manifest = live.manifest;
    if (!manifest || frame.header.gen !== manifest.generation) {
      // A frame for a model this page has not loaded yet: load it, drop the frame.
      if (frame.header.gen !== this.loadingGeneration) void this.loadScene(frame.header.gen);
      return;
    }
    live.setFrame(frame);
    telemetry.ingest(frame);
    const now = performance.now();
    if (now - this.lastTelemetryAt > 120) {
      this.lastTelemetryAt = now;
      useStore.setState({ telemetry: frame.header });
    }
  }

  private onMessage(text: string): void {
    let message: { type: string } & Record<string, unknown>;
    try {
      message = JSON.parse(text);
    } catch {
      return;
    }
    const store = useStore.getState();
    switch (message.type) {
      case "hello": {
        useStore.setState({ clientId: String(message.client ?? "") });
        const status = message.status as HostStatus | undefined;
        if (status && Object.keys(status).length) this.onStatus(status);
        void this.refreshLogs();
        break;
      }
      case "status":
        this.onStatus(message as unknown as HostStatus);
        break;
      case "scene":
        void this.loadScene(Number(message.generation));
        break;
      case "ack":
        this.onAck(message as unknown as Ack);
        break;
      case "command": {
        const record = message as unknown as CommandRecord;
        const commands = store.commands.filter((c) => c.id !== record.id);
        useStore.setState({ commands: [...commands.slice(-299), record] });
        break;
      }
      case "event": {
        const record = message as unknown as EventRecord;
        useStore.setState({ events: [...store.events.slice(-499), record] });
        if (record.level === "success") store.toast("ok", record.source, record.message);
        else if (record.level === "error" && record.source === "build") store.toast("err", "Build failed", record.message);
        break;
      }
      case "pong": {
        const t = Number(message.t);
        if (Number.isFinite(t)) useStore.setState({ latencyMs: Math.round(performance.now() - t) });
        break;
      }
    }
  }

  private onStatus(status: HostStatus): void {
    useStore.setState({ status });
    const manifest = live.manifest;
    if (status.phase === "ready" && status.generation > 0 && (!manifest || manifest.generation !== status.generation)) {
      if (status.generation !== this.loadingGeneration) void this.loadScene(status.generation);
    }
  }

  private onAck(ack: Ack): void {
    const store = useStore.getState();
    const isOurs = Boolean(store.pending[ack.id]) || this.waiters.has(ack.id);
    useStore.setState({ acks: { ...pruneAcks(store.acks), [ack.id]: ack } });
    if (TERMINAL.has(ack.status) || ack.status === "accepted") {
      const waiter = this.waiters.get(ack.id);
      if (waiter && (TERMINAL.has(ack.status) || !waiter.terminalOnly)) {
        this.waiters.delete(ack.id);
        waiter.resolve(ack);
      }
    }
    if (TERMINAL.has(ack.status) && store.pending[ack.id]) {
      const pending = { ...store.pending };
      delete pending[ack.id];
      useStore.setState({ pending });
    }
    if (isOurs && (ack.status === "rejected" || ack.status === "failed")) {
      store.toast("err", ack.status === "rejected" ? "Command rejected" : "Command failed", ack.message);
    }
  }

  async loadScene(generation: number): Promise<void> {
    if (this.loadingGeneration === generation) return;
    this.loadingGeneration = generation;
    useStore.setState({ sceneLoading: "Loading workcell geometry", sceneError: null });
    try {
      const manifest = await getJson<Manifest>("/api/scene/manifest");
      if (manifest.generation !== generation && generation >= 0) {
        // The service moved on while we asked; follow it.
        generation = manifest.generation;
        this.loadingGeneration = generation;
      }
      const geometry = await getBinary(`/api/scene/geometry?gen=${manifest.generation}`);
      live.geometry = geometry;
      live.manifest = manifest;
      live.frame = null;
      telemetry.reset();
      const store = useStore.getState();
      const keep = store.selected && manifest.subjects.some((s) => s.key === store.selected);
      const firstArm = manifest.arms[0] ? `arm:${manifest.arms[0].name}` : null;
      useStore.setState({ manifest, sceneLoading: null, selected: keep ? store.selected : firstArm });
    } catch (error) {
      useStore.setState({ sceneLoading: null, sceneError: error instanceof Error ? error.message : String(error) });
    } finally {
      if (this.loadingGeneration === generation) this.loadingGeneration = -1;
    }
  }

  async refreshInfo(): Promise<void> {
    try {
      const info = await getJson<ServiceInfo>("/api/info");
      useStore.setState({ info });
    } catch {
      /* the next reconnect asks again */
    }
  }

  async refreshLogs(): Promise<void> {
    try {
      const [events, commands] = await Promise.all([
        getJson<EventRecord[]>("/api/events?limit=300"),
        getJson<CommandRecord[]>("/api/commands?limit=200"),
      ]);
      useStore.setState({ events, commands });
    } catch {
      /* not fatal */
    }
  }
}

function pruneAcks(acks: Record<string, Ack>): Record<string, Ack> {
  const keys = Object.keys(acks);
  if (keys.length < 400) return acks;
  return Object.fromEntries(keys.slice(-200).map((k) => [k, acks[k]]));
}

export const bridge = new BridgeSocket();
