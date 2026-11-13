// MapDoc — 60fps SVG Wire Alignment, Smooth Pointer Dragging & Theme Helpers

const instances = new Map();

export function getTheme() {
    return localStorage.getItem('mapdoc-theme') || 'dark';
}

export function setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('mapdoc-theme', theme);
}

export async function copyToClipboard(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(text);
        return true;
    }
    return false;
}

export function initMapperStage(stageId, dotNetRef) {
    disposeMapperStage(stageId);

    const stage = document.getElementById(stageId);
    if (!stage) return;

    const svg = stage.querySelector('.mapper-svg-overlay');
    const sourcePane = stage.querySelector('.source-pane .tree-scroll');
    const targetPane = stage.querySelector('.target-pane .tree-scroll');
    const canvasPane = stage.querySelector('.canvas-pane');

    let rafPending = false;
    let dragWireState = null;
    let dragFunctoidState = null;
    let lastHoveredPortId = null;

    function requestSync() {
        if (rafPending) return;
        rafPending = true;
        requestAnimationFrame(() => {
            rafPending = false;
            syncWiresNow();
        });
    }

    function getPortPosition(portId) {
        const el = stage.querySelector(`[data-port-id="${CSS.escape(portId)}"]`);
        if (!el) return null;

        const stageRect = stage.getBoundingClientRect();
        const rect = el.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) return null;

        let x = rect.left + rect.width / 2 - stageRect.left;
        let y = rect.top + rect.height / 2 - stageRect.top;
        let clamped = false;

        // Clamp vertical coordinate to scrollable pane bounds if scrolled out of view
        const ownerScroll = el.closest('.tree-scroll, .canvas-pane');
        if (ownerScroll) {
            const paneRect = ownerScroll.getBoundingClientRect();
            const minY = paneRect.top - stageRect.top + 6;
            const maxY = paneRect.bottom - stageRect.top - 6;
            if (y < minY) {
                y = minY;
                clamped = true;
            } else if (y > maxY) {
                y = maxY;
                clamped = true;
            }

            if (ownerScroll.classList.contains('canvas-pane')) {
                const minX = paneRect.left - stageRect.left + 4;
                const maxX = paneRect.right - stageRect.left - 4;
                if (x < minX) { x = minX; clamped = true; }
                else if (x > maxX) { x = maxX; clamped = true; }
            }
        }

        return { x, y, clamped };
    }

    function buildBezierPath(x1, y1, x2, y2) {
        const dx = Math.abs(x2 - x1);
        const ctrlOffset = Math.max(42, Math.min(180, dx * 0.48));
        const cx1 = x1 + ctrlOffset;
        const cx2 = x2 - ctrlOffset;
        return `M ${x1.toFixed(1)} ${y1.toFixed(1)} C ${cx1.toFixed(1)} ${y1.toFixed(1)}, ${cx2.toFixed(1)} ${y2.toFixed(1)}, ${x2.toFixed(1)} ${y2.toFixed(1)}`;
    }

    function syncWiresNow() {
        if (!svg) return;
        const wireGroups = svg.querySelectorAll('g[data-wire-id]');
        for (const g of wireGroups) {
            const fromPort = g.getAttribute('data-from-port');
            const toPort = g.getAttribute('data-to-port');
            if (!fromPort || !toPort) continue;

            const p1 = getPortPosition(fromPort);
            const p2 = getPortPosition(toPort);

            if (!p1 || !p2) {
                g.style.display = 'none';
                continue;
            }

            g.style.display = '';
            const d = buildBezierPath(p1.x, p1.y, p2.x, p2.y);

            const paths = g.querySelectorAll('path');
            for (const path of paths) {
                path.setAttribute('d', d);
            }

            if (p1.clamped || p2.clamped) {
                g.setAttribute('data-clamped', 'true');
            } else {
                g.removeAttribute('data-clamped');
            }

            const badge = g.querySelector('.wire-comment-badge');
            if (badge) {
                const mx = (p1.x + p2.x) / 2;
                const my = (p1.y + p2.y) / 2;
                badge.setAttribute('transform', `translate(${mx.toFixed(1)}, ${my.toFixed(1)})`);
            }
        }
    }

    // ==================== POINTER DRAGGING: WIRES & FUNCTOID CARDS ====================

    function onPointerDown(e) {
        if (e.button !== 0) return;

        // 1. Check if starting from a Port Pin
        const portEl = e.target.closest('[data-port-id]');
        if (portEl && stage.contains(portEl)) {
            e.preventDefault();
            e.stopPropagation();

            const portId = portEl.getAttribute('data-port-id');
            const portSide = portEl.getAttribute('data-port-side'); // 'out' | 'in'
            const startPos = getPortPosition(portId);
            if (!startPos) return;

            const draftPath = svg.querySelector('.draft-wire-path');
            const tooltip = stage.querySelector('.draft-wire-tooltip');
            if (draftPath) {
                draftPath.style.display = '';
                draftPath.setAttribute('data-preview-severity', 'neutral');
                draftPath.setAttribute('d', buildBezierPath(startPos.x, startPos.y, startPos.x, startPos.y));
            }

            portEl.classList.add('port-dragging-origin');
            dragWireState = {
                originPortId: portId,
                originSide: portSide,
                originEl: portEl,
                startX: startPos.x,
                startY: startPos.y,
                draftPath,
                tooltip
            };
            lastHoveredPortId = null;
            return;
        }

        // 2. Check if dragging a Functoid Card header
        const dragHandle = e.target.closest('.functoid-drag-handle');
        if (dragHandle && canvasPane && canvasPane.contains(dragHandle)) {
            if (e.target.closest('button, input, textarea, select')) return;

            const card = dragHandle.closest('[data-functoid-id]');
            if (!card) return;

            e.preventDefault();
            const instanceId = card.getAttribute('data-functoid-id');
            const initialLeft = parseFloat(card.style.left) || 0;
            const initialTop = parseFloat(card.style.top) || 0;

            card.classList.add('functoid-dragging');
            dragFunctoidState = {
                instanceId,
                card,
                startClientX: e.clientX,
                startClientY: e.clientY,
                initialLeft,
                initialTop,
                currentLeft: initialLeft,
                currentTop: initialTop,
                moved: false
            };
        }
    }

    async function onPointerMove(e) {
        if (dragWireState) {
            const stageRect = stage.getBoundingClientRect();
            const curX = e.clientX - stageRect.left;
            const curY = e.clientY - stageRect.top;

            // Keep origin dynamic in case auto-scroll occurred
            const originNow = getPortPosition(dragWireState.originPortId) || { x: dragWireState.startX, y: dragWireState.startY };
            const d = dragWireState.originSide === 'out'
                ? buildBezierPath(originNow.x, originNow.y, curX, curY)
                : buildBezierPath(curX, curY, originNow.x, originNow.y);

            if (dragWireState.draftPath) {
                dragWireState.draftPath.setAttribute('d', d);
            }

            if (dragWireState.tooltip && dragWireState.tooltip.style.display !== 'none') {
                dragWireState.tooltip.style.left = `${Math.min(stageRect.width - 260, Math.max(12, curX + 16))}px`;
                dragWireState.tooltip.style.top = `${Math.max(12, curY - 36)}px`;
            }

            const targetEl = document.elementFromPoint(e.clientX, e.clientY)?.closest?.('[data-port-id]');
            const candidatePortId = targetEl && stage.contains(targetEl) ? targetEl.getAttribute('data-port-id') : null;
            const candidateSide = targetEl ? targetEl.getAttribute('data-port-side') : null;

            if (candidatePortId && candidatePortId !== dragWireState.originPortId && candidateSide !== dragWireState.originSide) {
                if (lastHoveredPortId !== candidatePortId) {
                    clearCandidatePortHighlights();
                    lastHoveredPortId = candidatePortId;
                    targetEl.classList.add('port-drop-hover');

                    const fromId = dragWireState.originSide === 'out' ? dragWireState.originPortId : candidatePortId;
                    const toId = dragWireState.originSide === 'out' ? candidatePortId : dragWireState.originPortId;

                    try {
                        const preview = await dotNetRef.invokeMethodAsync('PreviewWireDropAsync', fromId, toId);
                        if (dragWireState && lastHoveredPortId === candidatePortId && preview) {
                            if (dragWireState.draftPath) {
                                dragWireState.draftPath.setAttribute('data-preview-severity', preview.severity || 'valid');
                            }
                            targetEl.setAttribute('data-drop-severity', preview.severity || 'valid');
                            if (dragWireState.tooltip) {
                                dragWireState.tooltip.textContent = preview.message || 'Compatible types';
                                dragWireState.tooltip.setAttribute('data-severity', preview.severity || 'valid');
                                dragWireState.tooltip.style.display = 'block';
                                dragWireState.tooltip.style.left = `${Math.min(stageRect.width - 260, Math.max(12, curX + 16))}px`;
                                dragWireState.tooltip.style.top = `${Math.max(12, curY - 36)}px`;
                            }
                        }
                    } catch {
                        // Circuit busy or disposed
                    }
                }
            } else if (lastHoveredPortId !== null) {
                lastHoveredPortId = null;
                clearCandidatePortHighlights();
                if (dragWireState.draftPath) {
                    dragWireState.draftPath.setAttribute('data-preview-severity', 'neutral');
                }
                if (dragWireState.tooltip) {
                    dragWireState.tooltip.style.display = 'none';
                }
            }
            return;
        }

        if (dragFunctoidState) {
            const dx = e.clientX - dragFunctoidState.startClientX;
            const dy = e.clientY - dragFunctoidState.startClientY;
            if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
                dragFunctoidState.moved = true;
            }
            const nextLeft = Math.max(12, Math.round(dragFunctoidState.initialLeft + dx));
            const nextTop = Math.max(12, Math.round(dragFunctoidState.initialTop + dy));
            dragFunctoidState.currentLeft = nextLeft;
            dragFunctoidState.currentTop = nextTop;
            dragFunctoidState.card.style.left = `${nextLeft}px`;
            dragFunctoidState.card.style.top = `${nextTop}px`;
            syncWiresNow();
        }
    }

    async function onPointerUp(e) {
        if (dragWireState) {
            const state = dragWireState;
            dragWireState = null;

            state.originEl.classList.remove('port-dragging-origin');
            clearCandidatePortHighlights();
            if (state.draftPath) {
                state.draftPath.style.display = 'none';
                state.draftPath.removeAttribute('d');
            }
            if (state.tooltip) {
                state.tooltip.style.display = 'none';
            }

            const targetEl = document.elementFromPoint(e.clientX, e.clientY)?.closest?.('[data-port-id]');
            const candidatePortId = targetEl && stage.contains(targetEl) ? targetEl.getAttribute('data-port-id') : null;
            const candidateSide = targetEl ? targetEl.getAttribute('data-port-side') : null;

            if (candidatePortId && candidatePortId !== state.originPortId && candidateSide !== state.originSide) {
                const fromId = state.originSide === 'out' ? state.originPortId : candidatePortId;
                const toId = state.originSide === 'out' ? candidatePortId : state.originPortId;
                try {
                    await dotNetRef.invokeMethodAsync('CompleteWireDropAsync', fromId, toId);
                } catch {
                    // Ignore if circuit closed
                }
            }
            lastHoveredPortId = null;
            requestSync();
            return;
        }

        if (dragFunctoidState) {
            const state = dragFunctoidState;
            dragFunctoidState = null;
            state.card.classList.remove('functoid-dragging');

            if (state.moved) {
                try {
                    await dotNetRef.invokeMethodAsync('CommitFunctoidPositionAsync', state.instanceId, state.currentLeft, state.currentTop);
                } catch {
                    // Ignore
                }
            }
            requestSync();
        }
    }

    function clearCandidatePortHighlights() {
        const highlighted = stage.querySelectorAll('.port-drop-hover');
        for (const el of highlighted) {
            el.classList.remove('port-drop-hover');
            el.removeAttribute('data-drop-severity');
        }
    }

    // Toolbox HTML5 drag-over/drop onto canvasPane
    function onCanvasDragOver(e) {
        if (e.dataTransfer && e.dataTransfer.types.includes('text/mapdoc-functoid')) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
        }
    }

    async function onCanvasDrop(e) {
        const functoidDefId = e.dataTransfer?.getData('text/mapdoc-functoid');
        if (!functoidDefId || !canvasPane) return;
        e.preventDefault();
        const rect = canvasPane.getBoundingClientRect();
        const x = Math.max(20, Math.round(e.clientX - rect.left + canvasPane.scrollLeft - 75));
        const y = Math.max(20, Math.round(e.clientY - rect.top + canvasPane.scrollTop - 28));
        try {
            await dotNetRef.invokeMethodAsync('DropToolboxFunctoidAsync', functoidDefId, x, y);
        } catch {
            // Ignore
        }
    }

    stage.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);

    sourcePane?.addEventListener('scroll', requestSync, { passive: true });
    targetPane?.addEventListener('scroll', requestSync, { passive: true });
    canvasPane?.addEventListener('scroll', requestSync, { passive: true });
    canvasPane?.addEventListener('dragover', onCanvasDragOver);
    canvasPane?.addEventListener('drop', onCanvasDrop);

    const resizeObserver = new ResizeObserver(() => requestSync());
    resizeObserver.observe(stage);
    if (sourcePane) resizeObserver.observe(sourcePane);
    if (targetPane) resizeObserver.observe(targetPane);
    if (canvasPane) resizeObserver.observe(canvasPane);

    instances.set(stageId, {
        requestSync,
        cleanup: () => {
            stage.removeEventListener('pointerdown', onPointerDown);
            window.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);
            sourcePane?.removeEventListener('scroll', requestSync);
            targetPane?.removeEventListener('scroll', requestSync);
            canvasPane?.removeEventListener('scroll', requestSync);
            canvasPane?.removeEventListener('dragover', onCanvasDragOver);
            canvasPane?.removeEventListener('drop', onCanvasDrop);
            resizeObserver.disconnect();
        }
    });

    requestSync();
}

export function syncMapperStage(stageId) {
    const entry = instances.get(stageId);
    if (entry) {
        entry.requestSync();
    }
}

export function startToolboxDrag(event, functoidId) {
    if (event.dataTransfer) {
        event.dataTransfer.setData('text/mapdoc-functoid', functoidId);
        event.dataTransfer.effectAllowed = 'copy';
    }
}

export function disposeMapperStage(stageId) {
    const existing = instances.get(stageId);
    if (existing) {
        existing.cleanup();
        instances.delete(stageId);
    }
}
