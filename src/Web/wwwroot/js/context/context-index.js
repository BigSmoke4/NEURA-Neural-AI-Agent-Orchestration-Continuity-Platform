document.addEventListener('DOMContentLoaded', () => {
  const container = document.getElementById('context-graph');
  if (!container) return;
  const textAlternative = document.getElementById('context-graph-text-alt');
  if (!window.cytoscape) {
    if (textAlternative) textAlternative.textContent = 'Graph library did not load; use the context package table below.';
    return;
  }

  let elements;
  try {
    elements = JSON.parse(container.dataset.elements || '[]');
  } catch {
    if (textAlternative) textAlternative.textContent = 'Context graph data could not be loaded.';
    return;
  }
  if (!Array.isArray(elements) || elements.length === 0) {
    if (textAlternative) textAlternative.textContent = 'No context packages are available to graph.';
    return;
  }

  const cy = window.cytoscape({
    container,
    elements,
    style: [
      { selector: 'node', style: {
        'background-color': '#232830', 'border-width': 2, 'border-color': '#38f0e0',
        label: 'data(label)', color: '#e6ecf1', 'font-size': 9,
        'text-valign': 'bottom', 'text-margin-y': 6, width: 30, height: 30
      }},
      { selector: 'edge', style: {
        width: 1.5, 'line-color': '#2b3038', 'curve-style': 'bezier',
        'target-arrow-shape': 'triangle', 'target-arrow-color': '#2b3038',
        label: 'data(label)', 'font-size': 8, color: '#93a0ad'
      }}
    ],
    layout: { name: 'breadthfirst', directed: true, animate: false }
  });

  cy.on('tap', 'node[packageId]', (event) => {
    const packageId = event.target.data('packageId');
    if (textAlternative) textAlternative.textContent = 'Opening context package details.';
    const detailsUrl = container.dataset.detailsUrl;
    if (detailsUrl && packageId) {
      window.location.assign(`${detailsUrl.replace(/\/$/, '')}/${encodeURIComponent(packageId)}`);
    }
  });
  cy.on('tap', 'node', (event) => {
    if (!event.target.data('packageId'))
      if (textAlternative) textAlternative.textContent = `Selected: ${event.target.data('label')}`;
  });
});
