(() => {
    "use strict";

    document.querySelectorAll("[data-copy-material-link]").forEach(button => {
        button.addEventListener("click", async () => {
            const output = document.querySelector("[data-copy-link-result]");
            const materialUrl = new URL(button.dataset.copyMaterialLink, window.location.origin).href;
            try {
                if (navigator.clipboard && window.isSecureContext) {
                    await navigator.clipboard.writeText(materialUrl);
                } else {
                    const field = document.createElement("textarea");
                    field.value = materialUrl;
                    field.setAttribute("readonly", "");
                    field.setAttribute("aria-label", "Материал сілтемесі");
                    field.style.position = "fixed";
                    field.style.left = "-9999px";
                    document.body.appendChild(field);
                    field.select();
                    const copied = document.execCommand("copy");
                    field.remove();
                    button.focus();
                    if (!copied) throw new Error("copy-unavailable");
                }
                if (output) output.textContent = "Сілтеме көшірілді.";
            } catch {
                if (output) {
                    output.textContent = "Автоматты көшіру қолжетімсіз. Сілтемені мекенжай жолағынан көшіріңіз.";
                }
            }
        });
    });

    const panel = document.querySelector("[data-processing-status-url]");
    if (!panel || panel.dataset.processingActive !== "true") return;

    let timer;
    let failures = 0;
    let polls = 0;
    let stopped = false;
    const statusLabel = panel.querySelector("[data-ai-status-label]");
    const pollingNote = panel.querySelector("[data-polling-note]");
    const extractionLabel = panel.querySelector("[data-extraction-label]");
    const extractionCoverage = panel.querySelector("[data-extraction-coverage]");
    const labels = { Pending: "Кезекте", Processing: "Өңделуде", Completed: "Дайын", Failed: "Қате", NotConfigured: "AI қызметі бапталмаған", NotAnalyzed: "Талдау орындалмаған" };
    const active = state => state === "Pending" || state === "Processing";
    const schedule = () => {
        if (!stopped) timer = window.setTimeout(poll, document.hidden ? 12000 : 4000);
    };
    const poll = async () => {
        if (stopped) return;
        if (++polls > 150) {
            if (pollingNote) pollingNote.textContent = "Күйді жаңарту уақытша тоқтатылды. Соңғы нәтижені көру үшін бетті жаңартыңыз.";
            return;
        }
        const abort = new AbortController();
        const timeout = window.setTimeout(() => abort.abort(), 10000);
        try {
            const response = await fetch(panel.dataset.processingStatusUrl, {
                method: "GET", credentials: "same-origin", headers: { Accept: "application/json" }, signal: abort.signal
            });
            if (response.status === 401 || response.status === 403 || response.redirected) {
                stopped = true;
                if (pollingNote) pollingNote.textContent = "Күйді көру үшін жүйеге қайта кіріңіз.";
                return;
            }
            if (!response.ok) throw new Error("status-unavailable");
            const result = await response.json();
            const state = result.status;
            if (typeof state !== "string" || !Object.hasOwn(labels, state)) throw new Error("status-invalid");
            failures = 0;
            if (statusLabel) statusLabel.textContent = labels[state];
            if (extractionLabel && typeof result.extractionStatusLabel === "string") extractionLabel.textContent = result.extractionStatusLabel;
            if (extractionCoverage && Number.isSafeInteger(result.totalPages) && result.totalPages > 0) {
                extractionCoverage.textContent = `${result.totalPages} беттің ${result.extractedPages} беті · ${result.extractedCharacters} таңба`;
            }
            if (pollingNote) pollingNote.textContent = "Күй автоматты түрде жаңартылып отырады.";
            if (!active(state) && !active(result.extractionStatus)) {
                stopped = true;
                if (pollingNote) {
                    pollingNote.textContent = "Өңдеу аяқталды. ";
                    const open = document.createElement("a");
                    open.href = window.location.pathname + "#ai-analysis";
                    open.className = "text-link";
                    open.textContent = state === "Completed" ? "Нәтижені ашу →" : "Соңғы күйді ашу →";
                    pollingNote.appendChild(open);
                    open.addEventListener("click", event => {
                        event.preventDefault();
                        window.location.assign(window.location.pathname + "?result=latest#ai-analysis");
                    });
                }
                const loadingMessage = panel.querySelector(".analysis-loading");
                if (loadingMessage) loadingMessage.textContent = state === "Completed" ? "AI талдауы дайын. Төмендегі сілтеме арқылы қорытындыны ашыңыз." : (typeof result.message === "string" ? result.message : "Өңдеу аяқталды. Соңғы күйді ашыңыз.");
                return;
            }
        } catch {
            if (++failures >= 3) {
                stopped = true;
                if (pollingNote) pollingNote.textContent = "Күйді жаңарту мүмкін болмады. Біраздан кейін бетті жаңартыңыз.";
                return;
            }
        } finally {
            window.clearTimeout(timeout);
        }
        schedule();
    };
    window.addEventListener("pagehide", () => { stopped = true; window.clearTimeout(timer); });
    schedule();
})();
