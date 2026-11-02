"use strict";
// Orders already accepted server frames. No routes, combat or mission rules here.
class TurnPresentation {
    constructor() { this.controller = null; }
    canPlay(before, after, batch, commandId) {
        if (!before || !after || !batch || batch.commandId !== commandId ||
            batch.matchId !== before.matchId || after.matchId !== before.matchId ||
            batch.fromMatchVersion !== before.matchVersion || batch.toMatchVersion !== after.matchVersion ||
            after.matchVersion <= before.matchVersion || !batch.steps?.length) return false;
        let sequence = before.history.at(-1)?.sequence ?? 0;
        return batch.steps.every(step => step.sequence === ++sequence && step.frame?.units &&
            (step.kind !== "Move" || step.route?.length > 1)) &&
            sequence === after.history.at(-1)?.sequence;
    }
    skip() { this.controller?.abort(); }
    async play(batch, { animate, show, announce }, reducedMotion = false) {
        if (reducedMotion) return; // Show the saved final state without delayed motion.
        const controller = new AbortController();
        this.controller = controller;
        const duration = Math.min(230, 1900 / batch.steps.length);
        // A stalled browser animation never holds command input indefinitely.
        const deadline = setTimeout(() => controller.abort(), 2200);
        try {
            for (const step of batch.steps) {
                if (controller.signal.aborted) break;
                announce(step);
                let stop;
                const interrupted = new Promise(resolve => { stop = resolve; });
                controller.signal.addEventListener("abort", stop, { once: true });
                try { await Promise.race([animate(step, duration, controller.signal), interrupted]); }
                finally { controller.signal.removeEventListener("abort", stop); }
                if (!controller.signal.aborted) show(step);
            }
            return controller.signal.aborted;
        } finally {
            clearTimeout(deadline);
            controller.abort();
            if (this.controller === controller) this.controller = null;
        }
    }
}
