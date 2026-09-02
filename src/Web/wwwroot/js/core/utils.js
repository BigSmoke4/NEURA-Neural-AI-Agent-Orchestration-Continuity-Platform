window.Neura = window.Neura || {};

Neura.formatPercent = (ratio) => `${Math.round((ratio || 0) * 100)}%`;

Neura.announce = (message) => {
  const el = document.getElementById('graph-text-alternative');
  if (el) el.textContent = message;
};

Neura.pushEvent = (label, level = 'info') => {
  const feed = document.getElementById('event-feed');
  if (!feed) return;
  const li = document.createElement('li');
  const time = new Date().toLocaleTimeString();
  li.className = level === 'info' ? '' : `level-${level}`;
  li.innerHTML = `<b>${time}</b> ${label}`;
  feed.prepend(li);
  while (feed.children.length > 200) feed.removeChild(feed.lastChild);
};
