// Print pipeline (bisect test build: print feature only, on top of v1.4.5).
export interface PrintSource {
  getRawText: () => string;
  getFormattedHTML: () => Promise<string> | string;
}

function ensurePrintRoot(): HTMLElement {
  let root = document.getElementById("print-root");
  if (!root) {
    root = document.createElement("div");
    root.id = "print-root";
    document.body.appendChild(root);
  }
  return root;
}

export async function doPrint(mode: "source" | "formatted", src: PrintSource): Promise<void> {
  const root = ensurePrintRoot();

  if (mode === "source") {
    const pre = document.createElement("pre");
    pre.className = "print-source";
    pre.textContent = src.getRawText();
    root.replaceChildren(pre);
  } else {
    const html = await src.getFormattedHTML();
    const container = document.createElement("div");
    container.className = "milkdown print-formatted";
    container.innerHTML = html;
    root.replaceChildren(container);
  }

  document.body.classList.add("printing");
  document.body.dataset.printMode = mode;

  let cleaned = false;
  const cleanup = () => {
    if (cleaned) return;
    cleaned = true;
    window.removeEventListener("afterprint", cleanup);
    document.body.classList.remove("printing");
    delete document.body.dataset.printMode;
    root.replaceChildren();
  };
  window.addEventListener("afterprint", cleanup);

  requestAnimationFrame(() =>
    requestAnimationFrame(() => {
      try {
        window.print();
      } catch (err) {
        console.error("window.print failed", err);
        cleanup();
      }
    })
  );
}
