document.addEventListener('DOMContentLoaded', () => {
  const container = document.getElementById('knowledge-graph');
  if (!container) return;
  const textAlternative = document.getElementById('knowledge-graph-text-alt');
  if (!window.cytoscape) {
    if (textAlternative) textAlternative.textContent = 'Graph library did not load; use the knowledge tables below.';
    return;
  }

  let elements;
  try {
    elements = JSON.parse(container.dataset.elements || '[]');
  } catch {
    if (textAlternative) textAlternative.textContent = 'Knowledge graph data could not be loaded.';
    return;
  }
  if (!Array.isArray(elements) || elements.length === 0) {
    if (textAlternative) textAlternative.textContent = 'No knowledge graph data is available.';
    return;
  }

  const cy = window.cytoscape({
    container,
    elements,
    style: [
      { selector: 'node', style: {
        'background-color': '#232830', 'border-width': 2, 'border-color': '#4c8dff',
        label: 'data(label)', color: '#e6ecf1', 'font-size': 9,
        'text-valign': 'bottom', 'text-margin-y': 6, width: 34, height: 34
      }},
      { selector: 'edge', style: {
        width: 1.5, 'line-color': '#2b3038', 'curve-style': 'bezier',
        'target-arrow-shape': 'triangle', 'target-arrow-color': '#2b3038',
        label: 'data(label)', 'font-size': 8, color: '#93a0ad'
      }}
    ],
    layout: { name: 'cose', animate: false }
  });

  cy.on('tap', 'node', (event) => {
    const label = event.target.data('label');
    if (textAlternative) textAlternative.textContent = `Selected node: ${label}`;
  });
});
