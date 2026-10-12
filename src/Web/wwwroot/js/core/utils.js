window.Neura = window.Neura || {};

Neura.formatPercent = (ratio) => `${Math.round((ratio || 0) * 100)}%`;

Neura.announce = (message) => {
  const element = document.getElementById('graph-text-alternative');
  if (element) element.textContent = message;
};

// Event data can contain model output, provider error text, URLs, or agent
// names. Render it exclusively as text; never treat SignalR payloads as HTML.
Neura.pushEvent = (label, level = 'info') => {
  const feed = document.getElementById('event-feed');
  if (!feed) return;

  const item = document.createElement('li');
  const time = document.createElement('time');
  const message = document.createElement('span');
  time.dateTime = new Date().toISOString();
  time.textContent = new Date().toLocaleTimeString();
  message.textContent = String(label ?? '');
  item.className = level === 'info' ? '' : `level-${level}`;
  item.append(time, document.createTextNode(' '), message);
  feed.prepend(item);

  while (feed.children.length > 200) feed.removeChild(feed.lastChild);
};
