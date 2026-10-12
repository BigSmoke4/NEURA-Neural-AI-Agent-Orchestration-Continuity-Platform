document.addEventListener('DOMContentLoaded', () => {
  const container = document.getElementById('package-graph');
  if (!container) return;
  const textAlternative = document.getElementById('package-graph-text-alt');
  if (!window.cytoscape) {
    if (textAlternative) textAlternative.textContent = 'Graph library did not load; use the package detail lists below.';
    return;
  }

  let elements;
  try {
    elements = JSON.parse(container.dataset.elements || '[]');
  } catch {
    if (textAlternative) textAlternative.textContent = 'Package graph data could not be loaded.';
    return;
  }
  if (!Array.isArray(elements) || elements.length === 0) {
    if (textAlternative) textAlternative.textContent = 'No package graph data is available.';
    return;
  }

  const cy = window.cytoscape({
    container,
    elements,
    style: [
      { selector: 'node', style: {
        'background-color': '#232830', 'border-width': 2, 'border-color': '#4c8dff',
        label: 'data(label)', color: '#e6ecf1', 'font-size': 9,
        'text-valign': 'bottom', 'text-margin-y': 6, width: 26, height: 26,
        'text-wrap': 'wrap', 'text-max-width': '80px'
      }},
      { selector: 'edge', style: {
        width: 1.5, 'line-color': '#2b3038', 'curve-style': 'bezier',
        'target-arrow-shape': 'triangle', 'target-arrow-color': '#2b3038'
      }}
    ],
    layout: { name: 'breadthfirst', directed: true, animate: false, spacingFactor: 1.2 }
  });

  cy.on('tap', 'node', (event) => {
    if (textAlternative) textAlternative.textContent =
      `Selected: ${event.target.data('label')}`;
  });
});
