window.Neura = window.Neura || {};

Neura.Graph = (function () {
  let cy = null;

  function init(containerId) {
    cy = cytoscape({
      container: document.getElementById(containerId),
      elements: [
        { data: { id: 'orchestrator', label: 'ORCHESTRATOR' }, classes: 'orchestrator' },
      ],
      style: [
        { selector: 'node', style: {
            'background-color': '#232830',
            'border-width': 2,
            'border-color': '#38f0e0',
            'label': 'data(label)',
            'color': '#e6ecf1',
            'font-size': 10,
            'text-valign': 'bottom',
            'text-margin-y': 6,
            'width': 46, 'height': 46
        }},
        { selector: '.orchestrator', style: { 'background-color': '#1b1f24', 'border-color': '#4c8dff', 'width': 64, 'height': 64 } },
        { selector: '.agent-online', style: { 'border-color': '#38f088' } },
        { selector: '.agent-warning', style: { 'border-color': '#ffb020' } },
        { selector: '.agent-critical', style: { 'border-color': '#ff4d4f' } },
        { selector: 'edge', style: {
            'width': 2, 'line-color': '#2b3038', 'curve-style': 'bezier',
            'target-arrow-shape': 'triangle', 'target-arrow-color': '#2b3038'
        }},
        { selector: '.edge-active', style: { 'line-color': '#38f0e0', 'target-arrow-color': '#38f0e0', 'width': 3 } }
      ],
      layout: { name: 'concentric', concentric: () => 1, minNodeSpacing: 60 }
    });
    return cy;
  }

  function ensureAgentNode(id, label) {
    if (!cy.getElementById(id).length) {
      cy.add([
        { data: { id, label }, classes: 'agent-online' },
        { data: { id: `e-${id}`, source: 'orchestrator', target: id } }
      ]);
      cy.layout({ name: 'concentric', concentric: () => 1, minNodeSpacing: 70 }).run();
    }
    return cy.getElementById(id);
  }

  function ensureTaskNode(id, label) {
    if (!id || cy.getElementById(id).length) return cy.getElementById(id);
    cy.add({ data: { id, label: label || 'Task', kind: 'task' } });
    cy.layout({ name: 'concentric', concentric: () => 1, minNodeSpacing: 70 }).run();
    return cy.getElementById(id);
  }

  function ensureHandoffNode(id, fromId, toId) {
    if (fromId && !cy.getElementById(fromId).length) ensureAgentNode(fromId, 'Source agent');
    if (toId && !cy.getElementById(toId).length) ensureAgentNode(toId, 'Receiving agent');
    if (!id || !fromId || !toId) return;
    const edgeId = `handoff-${id}`;
    if (!cy.getElementById(edgeId).length) {
      cy.add({ data: { id: edgeId, source: fromId, target: toId, kind: 'handoff' } });
    }
    cy.layout({ name: 'concentric', concentric: () => 1, minNodeSpacing: 70 }).run();
  }

  function addErrorNode(id, label) {
    const errorId = `error-${id || Date.now()}`;
    if (!cy.getElementById(errorId).length) cy.add({ data: { id: errorId, label: label || 'Error', kind: 'error' } });
  }

  function setAgentState(id, state) {
    const node = cy.getElementById(id);
    if (!node.length) return;
    node.removeClass('agent-online agent-warning agent-critical');
    node.addClass(state);
  }

  function setFilter(filter) {
    if (!cy) return;
    const nodes = cy.nodes();
    const edges = cy.edges();
    nodes.forEach(node => {
      if (node.id() === 'orchestrator') { node.show(); return; }
      const classes = node.classes();
      const visible = filter === 'all' ||
        (filter === 'agents' && classes.some(c => c.startsWith('agent-'))) ||
        (filter === 'tasks' && node.data('kind') === 'task') ||
        (filter === 'handoffs' && node.data('kind') === 'handoff') ||
        (filter === 'errors' && node.data('kind') === 'error');
      visible ? node.show() : node.hide();
    });
    edges.forEach(edge => {
      const sourceVisible = edge.source().visible();
      const targetVisible = edge.target().visible();
      sourceVisible && targetVisible ? edge.show() : edge.hide();
    });
  }

  function pulseEdge(fromId, toId) {
    const edgeId = `e-${fromId}-${toId}`;
    let edge = cy.getElementById(edgeId);
    if (!edge.length) {
      cy.add({ data: { id: edgeId, source: fromId, target: toId } });
      edge = cy.getElementById(edgeId);
    }
    edge.addClass('edge-active');
    setTimeout(() => edge.removeClass('edge-active'), 900);
  }

  return { init, ensureAgentNode, ensureTaskNode, ensureHandoffNode, addErrorNode, setAgentState, pulseEdge, setFilter };
})();
