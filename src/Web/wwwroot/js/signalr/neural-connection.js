window.Neura = window.Neura || {};

Neura.Connection = (function () {
  let connection = null;

  const connectionStates = {
    connecting: { label: 'LIVE FEED CONNECTING', led: 'led--warning' },
    connected: { label: 'LIVE FEED CONNECTED', led: 'led--online' },
    reconnecting: { label: 'LIVE FEED RECONNECTING', led: 'led--warning' },
    disconnected: { label: 'LIVE FEED DISCONNECTED', led: 'led--critical' }
  };

  function setStatus(state) {
    const status = connectionStates[state] || connectionStates.disconnected;
    const label = document.getElementById('system-status-text');
    const led = document.getElementById('realtime-status-led');
    if (label) label.textContent = status.label;
    if (led) {
      led.classList.remove('led--unknown', 'led--online', 'led--warning', 'led--critical');
      led.classList.add(status.led);
    }
  }

  function start(onEvent) {
    if (!window.signalR) {
      setStatus('disconnected');
      Neura.pushEvent('SignalR client library did not load; live updates are unavailable.', 'critical');
      return;
    }

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/neural')
      .withAutomaticReconnect()
      .build();

    connection.on('NeuralEvent', (evt) => onEvent(evt));
    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(() => setStatus('connected'));
    connection.onclose(() => setStatus('disconnected'));

    setStatus('connecting');
    connection.start().then(() => {
      setStatus('connected');
    }).catch((err) => {
      setStatus('disconnected');
      console.error('NEURA hub connection failed:', err);
      Neura.pushEvent('SignalR connection failed — live updates unavailable.', 'critical');
    });
  }

  return { start };
})();
