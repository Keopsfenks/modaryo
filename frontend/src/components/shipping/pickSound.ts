/** Hata sesi: WebAudio ile kısa bir bip. Tarayıcı desteklemiyorsa ya da ses kapalıysa sessizce atlanır. */
export function beep(kind: "error" | "ok" = "error"): void {
    try {
        const Ctx = typeof window === "undefined" ? undefined : (window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext);
        if (!Ctx) return;
        const ctx = new Ctx();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = kind === "error" ? "sawtooth" : "sine";
        osc.frequency.value = kind === "error" ? 180 : 880;
        gain.gain.value = 0.15;
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.start();
        osc.stop(ctx.currentTime + (kind === "error" ? 0.35 : 0.08));
        osc.onended = () => void ctx.close();
    } catch {
        // Ses çalınamadıysa işlem etkilenmez.
    }
}
