window.Neura = window.Neura || {};

Neura.Graph = (function () {
  let cy = null;
  let layoutScheduled = false;
  const MaxGraphNodes = 800;

  function scheduleLayout() {
    if (!cy || layoutScheduled) return;
    layoutScheduled = true;
    requestAnimationFrame(() => {
      layoutScheduled = false;
      cy.layout({ name: 'concentric', concentric: (node) => node.data('kind') === 'orchestrator' ? 3 : 1,
        minNodeSpacing: 64, animate: false, fit: true, padding: 36 }).run();
    });
  }

  function updateAgentCount() {
    const counter = document.getElementById('agent-count-text');
    if (!counter || !cy) return;
    const count = cy.nodes().filter((node) => node.data('kind') === 'agent').length;
    counter.textContent = `AGENTS OBSERVED: ${count}`;
  }

  function touchNode(node) {
    if (node && node.length) node.data('lastActivityAt', Date.now());
  }

  function pruneGraphIfNeeded() {
    if (!cy) return;
    const candidates = [];
    cy.nodes().forEach((node) => {
      if (node.id() !== 'orchestrator') candidates.push(node);
    });
    const excess = candidates.length - MaxGraphNodes;
    if (excess <= 0) return;

    candidates.sort((left, right) =>
      Number(left.data('lastActivityAt') || 0) - Number(right.data('lastActivityAt') || 0));
    candidates.slice(0, excess).forEach((node) => node.remove());
    updateAgentCount();
  }

  function init(containerId) {
    const container = document.getElementById(containerId);
    if (!container || !window.cytoscape) return null;

    cy = cytoscape({
      container,
      elements: [{ data: { id: 'orchestrator', label: 'NEURA ORCHESTRATOR', kind: 'orchestrator', status: 'online' }, classes: 'orchestrator' }],
      minZoom: 0.25,
      maxZoom: 2.5,
      wheelSensitivity: 0.18,
      style: [
        { selector: 'node', style: {
          'background-color': '#232830', 'border-width': 2, 'border-color': '#52616b',
          'label': 'data(label)', 'color': '#e6ecf1', 'font-size': 10,
          'text-valign': 'bottom', 'text-margin-y': 7,
          'text-wrap': 'wrap', 'text-max-width': 120,
          'width': 46, 'height': 46, 'font-family': 'Segoe UI, sans-serif'
        }},
        { selector: '.orchestrator', style: {
          'background-color': '#171d23', 'border-color': '#4c8dff', 'border-width': 3,
          'width': 72, 'height': 72, 'font-size': 11, 'font-weight': 700,
          'shape': 'round-rectangle', 'padding': 7
        }},
        { selector: '.agent-online', style: { 'border-color': '#38f088' }},
        { selector: '.agent-busy', style: { 'border-color': '#38f0e0', 'border-width': 3 }},
        { selector: '.agent-warning', style: { 'border-color': '#ffb020', 'border-width': 3 }},
        { selector: '.agent-critical', style: { 'border-color': '#ff4d4f', 'border-width': 4 }},
        { selector: '.agent-failed', style: { 'border-color': '#ff4d4f', 'background-color': '#351d22' }},
        { selector: '.task-node', style: { 'shape': 'round-rectangle', 'background-color': '#202730', 'border-color': '#4c8dff', 'width': 58, 'height': 34 }},
        { selector: '.handoff-node', style: { 'shape': 'diamond', 'background-color': '#342b1b', 'border-color': '#ffb020', 'width': 34, 'height': 34 }},
        { selector: '.memory-node', style: { 'shape': 'hexagon', 'background-color': '#29253b', 'border-color': '#b99aff', 'width': 42, 'height': 42 }},
        { selector: '.memory-context-edge', style: { 'line-color': '#b99aff', 'target-arrow-color': '#b99aff', 'line-style': 'dashed' }},
        { selector: '.error-node', style: { 'shape': 'octagon', 'background-color': '#351d22', 'border-color': '#ff4d4f' }},
        { selector: 'edge', style: {
          'width': 1.5, 'line-color': '#3a424c', 'curve-style': 'bezier',
          'target-arrow-shape': 'triangle', 'target-arrow-color': '#3a424c',
          'label': 'data(label)', 'font-size': 8, 'color': '#93a0ad', 'text-background-opacity': 0
        }},
        { selector: '.edge-active', style: {
          'line-color': '#38f0e0', 'target-arrow-color': '#38f0e0', 'width': 3,
          'line-style': 'dashed', 'line-dash-pattern': [8, 4]
        }},
        { selector: ':selected', style: { 'border-color': '#ffffff', 'border-width': 4, 'overlay-opacity': 0.12 }},
        { selector: '.filtered-out', style: { 'display': 'none' }}
      ],
      layout: { name: 'concentric', concentric: (node) => node.data('kind') === 'orchestrator' ? 3 : 1,
        minNodeSpacing: 64, animate: false, fit: true, padding: 36 }
    });

    cy.on('tap', 'node', (event) => {
      window.dispatchEvent(new CustomEvent('neura:node-selected', { detail: event.target.data() }));
    });
    cy.on('tap', 'edge', (event) => {
      window.dispatchEvent(new CustomEvent('neura:edge-selected', { detail: event.target.data() }));
    });
    updateAgentCount();
    return cy;
  }

  function ensureAgentNode(id, label, status) {
    if (!cy || !id) return null;
    id = String(id);
    let node = cy.getElementById(id);
    if (!node.length) {
      cy.add({ data: { id, label: label || 'Agent', kind: 'agent', status: status || 'online', lastActivityAt: Date.now() }, classes: 'agent-online' });
      const edgeId = `e-orchestrator-${id}`;
      cy.add({ data: { id: edgeId, source: 'orchestrator', target: id, kind: 'agent-link', label: 'assigned' } });
      node = cy.getElementById(id);
      scheduleLayout();
    } else if (label) {
      node.data('label', label);
    }
    if (status) setAgentState(id, statusClass(status));
    touchNode(node);
    pruneGraphIfNeeded();
    updateAgentCount();
    return node;
  }

  function ensureTaskNode(id, label, missionId) {
    if (!cy || !id) return null;
    id = String(id);
    let node = cy.getElementById(id);
    if (!node.length) {
      cy.add({ data: { id, label: label || 'Task', kind: 'task', missionId: missionId || null, lastActivityAt: Date.now() }, classes: 'task-node' });
      cy.add({ data: { id: `e-orchestrator-${id}`, source: 'orchestrator', target: id, kind: 'task-link', label: 'task' } });
      node = cy.getElementById(id);
      scheduleLayout();
    } else if (label) {
      node.data('label', label);
    }
    touchNode(node);
    pruneGraphIfNeeded();
    return node;
  }

  function ensureMemoryNode(memoryId, type, missionId, taskId, taskTitle) {
    if (!cy || !memoryId) return null;
    const recordId = String(memoryId);
    const nodeId = `memory-${recordId}`;
    const label = `MEMORY · ${String(type || 'Reference')}`;
    let node = cy.getElementById(nodeId);
    if (!node.length) {
      cy.add({
        data: { id: nodeId, label, kind: 'memory', recordId, memoryType: type || 'Reference', missionId: missionId || null, lastActivityAt: Date.now() },
        classes: 'memory-node'
      });
      node = cy.getElementById(nodeId);
    } else {
      node.data({ label, memoryType: type || 'Reference', missionId: missionId || null });
    }

    if (taskId) {
      const taskNode = ensureTaskNode(taskId, taskTitle || 'Task', missionId);
      if (taskNode) {
        const edgeId = `e-${nodeId}-${String(taskId)}`;
        if (!cy.getElementById(edgeId).length) {
          cy.add({
            data: { id: edgeId, source: nodeId, target: String(taskId), kind: 'memory-context', label: 'untrusted context' },
            classes: 'memory-context-edge'
          });
        }
      }
    }
    touchNode(node);
    pruneGraphIfNeeded();
    scheduleLayout();
    return node;
  }

  function ensureHandoffNode(id, fromId, toId, details = {}) {
    if (!cy || !id || !fromId || !toId) return null;
    fromId = String(fromId);
    toId = String(toId);
    ensureAgentNode(fromId, details.fromName || 'Source agent');
    ensureAgentNode(toId, details.toName || 'Receiving agent');

    const nodeId = `handoff-${id}`;
    let node = cy.getElementById(nodeId);
    if (!node.length) {
      cy.add({ data: { ...details, id: nodeId, label: 'HANDOFF', kind: 'handoff', handoffId: String(id), lastActivityAt: Date.now() }, classes: 'handoff-node' });
      cy.add([
        { data: { id: `${nodeId}-from`, source: fromId, target: nodeId, kind: 'handoff-link', label: 'context' } },
        { data: { id: `${nodeId}-to`, source: nodeId, target: toId, kind: 'handoff-link', label: 'resume' } }
      ]);
      node = cy.getElementById(nodeId);
    } else {
      node.data({ ...details, kind: 'handoff', handoffId: String(id), lastActivityAt: Date.now() });
    }
    touchNode(node);
    pruneGraphIfNeeded();
    scheduleLayout();
    return node;
  }

  function addErrorNode(id, label, missionId) {
    if (!cy) return null;
    const errorId = `error-${id || Date.now()}`;
    let node = cy.getElementById(errorId);
    if (!node.length) {
      cy.add({ data: { id: errorId, label: label || 'Execution error', kind: 'error', missionId: missionId || null, lastActivityAt: Date.now() }, classes: 'error-node' });
      cy.add({ data: { id: `e-orchestrator-${errorId}`, source: 'orchestrator', target: errorId, kind: 'error-link' } });
      node = cy.getElementById(errorId);
      scheduleLayout();
    }
    touchNode(node);
    pruneGraphIfNeeded();
    return node;
  }

  function statusClass(status) {
    const normalized = String(status || '').toLowerCase();
    if (normalized.includes('critical') || normalized.includes('exhaust')) return 'agent-critical';
    if (normalized.includes('warning')) return 'agent-warning';
    if (normalized.includes('fail') || normalized.includes('offline')) return 'agent-failed';
    if (normalized.includes('execut') || normalized.includes('busy') || normalized.includes('think')) return 'agent-busy';
    return 'agent-online';
  }

  function setAgentState(id, state) {
    if (!cy || !id) return;
    const node = cy.getElementById(String(id));
    if (!node.length) return;
    node.removeClass('agent-online agent-busy agent-warning agent-critical agent-failed');
    node.addClass(state);
    node.data('status', state);
    touchNode(node);
  }

  function pulseEdge(fromId, toId) {
    if (!cy || !fromId || !toId) return;
    const source = String(fromId), target = String(toId);
    let edge = cy.edges().filter((candidate) =>
      candidate.source().id() === source && candidate.target().id() === target);
    if (!edge.length) {
      const edgeId = `e-${source}-${target}`;
      if (!cy.getElementById(edgeId).length)
        cy.add({ data: { id: edgeId, source, target, kind: 'transfer' } });
      edge = cy.getElementById(edgeId);
    }
    edge.addClass('edge-active');
    window.setTimeout(() => edge.removeClass('edge-active'), 900);
  }

  function setFilter(filter) {
    if (!cy) return;
    const showNode = (node) => {
      const kind = node.data('kind');
      if (kind === 'orchestrator') return true;
      if (filter === 'all') return true;
      if (filter === 'agents') return kind === 'agent';
      if (filter === 'tasks') return kind === 'task';
      if (filter === 'handoffs') return kind === 'handoff' || kind === 'agent';
      if (filter === 'memories') return kind === 'memory' || kind === 'task';
      if (filter === 'errors') return kind === 'error';
      return true;
    };
    cy.nodes().forEach((node) => showNode(node) ? node.removeClass('filtered-out') : node.addClass('filtered-out'));
    cy.edges().forEach((edge) => {
      const visible = !edge.source().hasClass('filtered-out') && !edge.target().hasClass('filtered-out');
      visible ? edge.removeClass('filtered-out') : edge.addClass('filtered-out');
    });
  }

  function zoomIn() { if (cy) cy.zoom({ level: Math.min(cy.maxZoom(), cy.zoom() * 1.2), renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } }); }
  function zoomOut() { if (cy) cy.zoom({ level: Math.max(cy.minZoom(), cy.zoom() / 1.2), renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } }); }
  function fit() { if (cy) cy.fit(undefined, 36); }

  return { init, ensureAgentNode, ensureTaskNode, ensureMemoryNode, ensureHandoffNode, addErrorNode, setAgentState, pulseEdge, setFilter, zoomIn, zoomOut, fit };
})();
