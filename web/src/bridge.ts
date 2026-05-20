// Bridge to the C# host (WebView2 chrome.webview).
// Posts typed messages and dispatches inbound messages to handlers.

export interface HostMessage {
  type: string;
  payload?: unknown;
}

type Handler = (payload: any) => void;

const handlers: Map<string, Handler> = new Map();

export function on(type: string, handler: Handler): void {
  handlers.set(type, handler);
}

export function post(type: string, payload?: unknown): void {
  try {
    (window as any).chrome?.webview?.postMessage({ type, payload });
  } catch {
    console.log("[host]", type, payload);
  }
}

function dispatch(msg: HostMessage): void {
  if (!msg || !msg.type) return;
  const h = handlers.get(msg.type);
  if (h) {
    try {
      h(msg.payload || {});
    } catch (err) {
      console.error("handler error", msg.type, err);
    }
  }
}

window.addEventListener("message", (ev) => dispatch((ev.data as HostMessage) || ({} as HostMessage)));
const cw = (window as any).chrome?.webview;
if (cw && typeof cw.addEventListener === "function") {
  cw.addEventListener("message", (ev: any) => dispatch((ev.data as HostMessage) || ({} as HostMessage)));
}
