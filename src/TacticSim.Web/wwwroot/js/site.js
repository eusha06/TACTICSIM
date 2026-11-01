"use strict";
(() => {
    const app = document.querySelector("#game-app");
    if (!app) return;
    const byId = id => document.getElementById(id);
    const board = byId("board");
    const token = app.querySelector('[name="__RequestVerificationToken"]').value;
    // Original local SVG symbols are presentation only. The server supplies all terrain,
    // occupancy, legal actions and health. Neighbor checks below only draw shorelines.
    const artUrl = app.dataset.artUrl;
    function illustration(symbol, className) {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 100 100");
        svg.setAttribute("class", className);
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");
        const use = document.createElementNS(svg.namespaceURI, "use");
        use.setAttribute("href", `${artUrl}#${symbol}`);
        svg.append(use);
        return svg;
    }
    function terrainArt(terrain, x, y) {
        const art = illustration(terrain.toLowerCase(), "terrain-art");
        if (terrain === "Water") {
            for (const [dx, dy, rotation] of [[0, -1, 0], [1, 0, 90], [0, 1, 180], [-1, 0, 270]]) {
                const nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < 8 && ny >= 0 && ny < 8 && state.tiles[ny * 8 + nx] === "Water") continue;
                const shore = document.createElementNS(art.namespaceURI, "use");
                shore.setAttribute("href", `${artUrl}#shore`);
                shore.setAttribute("transform", `rotate(${rotation} 50 50)`);
                art.append(shore);
            }
        }
        return art;
    }
    let state = null;
    let selectedId = null;
    let busy = false;
    let pending = null; // Kept after a network failure, so Retry uses the SAME command ID.
    let retryAction = load;

    function element(tag, className, text) {
        const node = document.createElement(tag);
        node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }
    function error(message, retry = load) {
        byId("error-text").textContent = message;
        byId("error").hidden = false;
        retryAction = retry;
    }
    function accept(next) {
        if (!next || (state && next.version < state.version)) return;
        state = next;
        if (!state.units.some(u => u.id === selectedId && u.health > 0)) selectedId = null;
    }
    async function load() {
        if (busy) return;
        if (pending) return submit();
        busy = true;
        byId("error").hidden = true;
        render();
        try {
            const response = await fetch(app.dataset.stateUrl, { credentials: "same-origin", cache: "no-store" });
            const data = await response.json();
            if (response.status === 401) {
                error(data.error, () => location.reload());
                return;
            }
            if (!response.ok) throw new Error(data.error || "Could not retrieve the battlefield.");
            accept(data.state);
        } catch (failure) {
            error(failure.message || "Connection lost. Retry to retrieve your saved match.");
        } finally {
            busy = false;
            render();
        }
        if (!state && byId("error").hidden) await command("new");
    }
    async function command(kind, fields = {}) {
        if (busy || pending) return;
        pending = { commandId: crypto.randomUUID(), expectedVersion: state?.version ?? 0,
            matchId: state?.matchId ?? null, expectedMatchVersion: state?.matchVersion ?? null, kind, ...fields };
        await submit();
    }
    async function submit() {
        if (busy || !pending) return;
        // Disabling cells during a save removes focus. Restore the originating cell
        // afterwards so keyboard users can continue with arrows/Enter.
        const commandCell = document.activeElement?.dataset.cell;
        busy = true;
        byId("error").hidden = true;
        render();
        let refresh = false;
        try {
            const response = await fetch(app.dataset.commandUrl, {
                method: "POST", credentials: "same-origin", cache: "no-store",
                headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
                body: JSON.stringify(pending)
            });
            if (!response.headers.get("content-type")?.includes("application/json")) {
                pending = null;
                error("Your session token expired. Reload this page and try again.", () => location.reload());
                return;
            }
            const data = await response.json();
            if (response.status >= 500) {
                error(data.error || "The server could not save your action. Retry safely.", submit);
                return;
            }
            refresh = !!data.state && !!state && data.state.version < state.version;
            accept(data.state);
            pending = null;
            if (!response.ok) error(data.error || "That action could not be completed.", response.status === 401 ? () => location.reload() : load);
        } catch {
            error("Connection interrupted. Retry this command safely; it will only apply once.", submit);
        } finally {
            busy = false;
            render();
        }
        if (commandCell !== undefined) board.querySelector(`[data-cell="${commandCell}"]`)?.focus({ preventScroll: true });
        if (refresh) await load();
    }

    function render() {
        const locked = busy || !!pending;
        byId("new-match").disabled = locked;
        byId("end-turn").disabled = locked || !state || !!state.winner || state.currentSide !== "Human";
        board.setAttribute("aria-busy", String(busy));
        if (!state) return;
        const selected = state.units.find(u => u.id === selectedId);
        const actionable = selected?.side === "Human" && selected.health > 0 && !state.winner;
        // These lists come from C#. JavaScript never calculates a route, range, or damage.
        const moves = actionable ? selected.legalMoves : [];
        const targets = actionable ? selected.legalTargets : [];
        const focusCell = document.activeElement?.dataset.cell;
        const cells = document.createDocumentFragment();
        for (let index = 0; index < state.tiles.length; index++) {
            const x = index % 8, y = Math.floor(index / 8);
            const terrain = state.tiles[index];
            const unit = state.units.find(u => u.health > 0 && u.x === x && u.y === y);
            const move = moves.find(m => m.x === x && m.y === y);
            const target = unit && targets.includes(unit.id);
            const cell = element("button", `cell ${terrain.toLowerCase()}`);
            cell.type = "button";
            cell.dataset.cell = String(index);
            cell.disabled = locked;
            if (move) cell.classList.add("legal-move");
            if (target) cell.classList.add("attack-target");
            if (unit?.id === selectedId) cell.classList.add(unit.side === "Human" ? "selected" : "inspecting");
            const coordinate = `${String.fromCharCode(65 + x)}${y + 1}`;
            const label = [coordinate, terrain, unit ? `${unit.side === "Human" ? "Your" : "Opponent"} ${unit.type}, ${unit.health} of ${unit.maxHealth} health` : "empty"];
            if (move) label.push(`legal move, cost ${move.cost}`);
            if (target) label.push("legal attack target");
            cell.setAttribute("aria-label", label.join(", "));
            cell.setAttribute("aria-pressed", String(unit?.id === selectedId));
            cell.append(terrainArt(terrain, x, y));
            if (unit) {
                const tokenNode = element("span", `unit-token ${unit.side.toLowerCase()} ${unit.hasAttacked ? "spent" : ""}`);
                tokenNode.setAttribute("aria-hidden", "true");
                tokenNode.append(illustration(unit.type.toLowerCase(), "unit-art"), element("span", "unit-name", unit.type));
                const health = element("span", "health-track");
                const remaining = element("span", "health-fill");
                remaining.style.width = `${100 * unit.health / unit.maxHealth}%`;
                health.append(remaining);
                tokenNode.append(health, element("span", "unit-hp", `${unit.health}/${unit.maxHealth}`));
                cell.append(tokenNode);
            }
            if (move) cell.append(element("span", "move-cost", move.cost));
            cell.addEventListener("click", () => choose(x, y, unit));
            cells.append(cell);
        }
        board.replaceChildren(cells);
        if (focusCell !== undefined && !locked) board.querySelector(`[data-cell="${focusCell}"]`)?.focus({ preventScroll: true });
        byId("turn-label").textContent = `TURN ${String(state.turnNumber).padStart(2, "0")} · ${state.winner ? "COMPLETE" : "YOUR COMMAND"}`;
        byId("turn-title").textContent = busy && pending?.kind === "endTurn" ? "Opponent's turn…" : state.winner ? "Match complete" : "Your turn";
        byId("turn-number").textContent = `T${String(state.turnNumber).padStart(2, "0")}`;
        byId("turn-description").textContent = state.winner ? "Start a fresh operation when you're ready." : busy ? "Saving your command…" : "Move your squad, pick your targets, hold the ground.";
        byId("board-hint").textContent = state.winner ? "Operation complete" : selected ? `${selected.type} #${selected.id} selected${actionable ? " · choose a cell or target" : " · opponent unit"}` : "Select a blue unit to begin";
        byId("victory").hidden = !state.winner;
        if (state.winner) {
            byId("victory").replaceChildren(element("b", "", state.winner === "Human" ? "Victory. The field is yours." : "Defeat. Your squad was eliminated."), document.createTextNode(`Completed on turn ${state.turnNumber}. Start a new match to try another plan.`));
        }
        renderDetails(selected);
        renderSquad();
        renderHistory();
    }
    function choose(x, y, unit) {
        if (busy || pending) return;
        const selected = state.units.find(u => u.id === selectedId);
        if (!state.winner && selected?.side === "Human" && unit?.side === "Computer") {
            command("attack", { unitId: selected.id, targetId: unit.id });
        } else if (unit) {
            selectedId = selectedId === unit.id ? null : unit.id;
            render();
        } else if (!state.winner && selected?.side === "Human") {
            command("move", { unitId: selected.id, x, y });
        } else {
            selectedId = null;
            render();
        }
    }
    function renderDetails(unit) {
        const details = byId("unit-details");
        details.replaceChildren();
        if (!unit) {
            details.append(element("p", "empty-state", "Select a blue unit on the board to see its stats and legal actions."));
            return;
        }
        const name = element("div", `intel-name ${unit.side.toLowerCase()}`);
        name.append(illustration(unit.type.toLowerCase(), "intel-art"), document.createTextNode(unit.type));
        name.append(element("span", `unit-side ${unit.side.toLowerCase()}`, unit.side === "Human" ? "YOUR UNIT" : "OPPONENT"));
        const stats = element("div", "intel-stats");
        for (const [label, value] of [["HEALTH", `${unit.health}/${unit.maxHealth}`], ["MOVEMENT", unit.movementPoints], ["RANGE", unit.minimumRange === unit.maximumRange ? unit.minimumRange : `${unit.minimumRange}–${unit.maximumRange}`]]) {
            const stat = element("div", "");
            stat.append(element("span", "stat-label", label), element("span", "stat-value", value));
            stats.append(stat);
        }
        details.append(name, stats);
        for (const [label, value] of [["Move", unit.hasAttacked ? "Locked after attack" : unit.hasMoved ? "Used" : "Available"], ["Attack", unit.hasAttacked ? "Used" : "Available"], ["Base damage", unit.baseDamage]]) {
            const row = element("div", "action-status", label);
            row.append(element("b", "", value)); details.append(row);
        }
        details.append(element("p", "intel-location", `${String.fromCharCode(65 + unit.x)}${unit.y + 1} / ${state.tiles[unit.y * 8 + unit.x]}`));
    }
    function renderSquad() {
        const squad = byId("squad"); squad.replaceChildren();
        const humans = state.units.filter(u => u.side === "Human");
        byId("squad-count").textContent = `${humans.filter(u => u.health > 0).length} / 3 ACTIVE`;
        for (const unit of humans) {
            const row = element("button", `squad-row ${unit.id === selectedId ? "active" : ""} ${unit.health === 0 ? "dead" : ""}`);
            row.type = "button"; row.disabled = busy || !!pending || unit.health === 0;
            row.setAttribute("aria-label", `Select your ${unit.type}, ${unit.health} health`);
            const label = element("span", "");
            label.append(element("div", "squad-name", unit.type), element("div", "squad-state", unit.health === 0 ? "Eliminated" : unit.hasAttacked ? "Attack used" : unit.hasMoved ? "Move used" : "Ready for orders"));
            row.append(illustration(unit.type.toLowerCase(), "squad-art"), label, element("span", "squad-health", `${unit.health}/${unit.maxHealth}`));
            row.addEventListener("click", () => { selectedId = unit.id; render(); });
            squad.append(row);
        }
    }
    function renderHistory() {
        const history = byId("history");
        history.replaceChildren();
        byId("history-count").textContent = `${state.history.length} entries`;
        for (const entry of [...state.history].reverse()) {
            const row = element("li", "history-entry");
            row.append(element("span", "history-turn", `T${String(entry.turn).padStart(2, "0")}`), element("span", `history-dot ${entry.side.toLowerCase()}`), element("span", "", entry.text));
            history.append(row);
        }
        const results = byId("results"); results.replaceChildren();
        for (const result of state.results) results.append(element("li", "", `${result.winner === "Human" ? "Victory" : "Defeat"} · ${result.turns} turns · ${new Date(result.completedAt).toLocaleDateString()}`));
        if (!state.results.length) results.append(element("li", "", "No completed matches yet."));
    }

    byId("end-turn").addEventListener("click", () => command("endTurn"));
    byId("new-match").addEventListener("click", () => state ? byId("new-dialog").showModal() : command("new"));
    byId("cancel-new").addEventListener("click", () => byId("new-dialog").close());
    byId("confirm-new").addEventListener("click", () => { byId("new-dialog").close(); selectedId = null; command("new"); });
    byId("retry").addEventListener("click", () => retryAction());
    board.addEventListener("keydown", event => {
        if (event.key === "Escape") { selectedId = null; render(); return; }
        const index = Number(event.target.dataset.cell);
        if (!Number.isInteger(index)) return;
        const x = index % 8, y = Math.floor(index / 8);
        const next = { ArrowLeft: x > 0 ? index - 1 : index, ArrowRight: x < 7 ? index + 1 : index, ArrowUp: y > 0 ? index - 8 : index, ArrowDown: y < 7 ? index + 8 : index }[event.key];
        if (next !== undefined) { event.preventDefault(); board.querySelector(`[data-cell="${next}"]`)?.focus(); }
    });
    document.addEventListener("visibilitychange", () => { if (!document.hidden && !busy && !pending) load(); });
    load();
})();
