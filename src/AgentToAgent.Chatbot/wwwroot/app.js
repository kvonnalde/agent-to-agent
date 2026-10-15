(() => {
  const messagesEl = document.getElementById('messages');
  const formEl = document.getElementById('chat-form');
  const inputEl = document.getElementById('chat-input');
  const sendButton = document.getElementById('send-button');

  const STARTER_PROMPTS = [
    'What is Atlas / AI Hub?',
    'Show me use cases for data analytics',
    'How do I request access?',
  ];

  /** @type {{role: 'user'|'assistant', content: string}[]} */
  const history = [];

  function formatTime(date = new Date()) {
    return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  }

  // Builds the row/avatar/bubble DOM structure expected by styles.css.
  function appendMessage(role, text, options = {}) {
    const row = document.createElement('div');
    row.className = `message-row ${role}`;

    const avatar = document.createElement('div');
    avatar.className = 'avatar';
    avatar.textContent = role === 'assistant' ? 'A' : 'U';
    row.appendChild(avatar);

    const col = document.createElement('div');
    col.className = 'message-col';

    const bubble = document.createElement('div');
    bubble.className = 'message';

    if (options.badge) {
      const badge = document.createElement('div');
      badge.className = 'message-badge';
      badge.textContent = options.badge;
      bubble.appendChild(badge);
    }

    if (options.pending) {
      const dots = document.createElement('span');
      dots.className = 'typing-dots';
      dots.innerHTML = '<span></span><span></span><span></span>';
      bubble.appendChild(dots);
    } else {
      const textNode = document.createElement('div');
      textNode.className = 'message-text';
      textNode.textContent = text;
      bubble.appendChild(textNode);
    }

    if (options.matches && options.matches.length > 0) {
      const matchList = document.createElement('div');
      matchList.className = 'message-matches';
      for (const match of options.matches) {
        const link = document.createElement('a');
        link.textContent = match.title || match.id || 'Use case';
        if (match.link) {
          link.href = match.link;
          link.target = '_blank';
          link.rel = 'noopener noreferrer';
        }
        matchList.appendChild(link);
      }
      bubble.appendChild(matchList);
    }

    col.appendChild(bubble);

    if (!options.pending) {
      const time = document.createElement('div');
      time.className = 'message-time';
      time.textContent = formatTime();
      col.appendChild(time);
    }

    row.appendChild(col);
    messagesEl.appendChild(row);
    messagesEl.scrollTop = messagesEl.scrollHeight;
    return row;
  }

  function renderSuggestions() {
    const wrap = document.createElement('div');
    wrap.className = 'suggestions';
    for (const prompt of STARTER_PROMPTS) {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'suggestion-chip';
      chip.textContent = prompt;
      chip.addEventListener('click', () => {
        wrap.remove();
        inputEl.value = prompt;
        formEl.requestSubmit();
      });
      wrap.appendChild(chip);
    }
    messagesEl.appendChild(wrap);
    messagesEl.scrollTop = messagesEl.scrollHeight;
  }

  async function sendMessage(message) {
    appendMessage('user', message);
    history.push({ role: 'user', content: message });

    const pendingRow = appendMessage('assistant', '', { pending: true });

    inputEl.disabled = true;
    sendButton.disabled = true;
    try {
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message, history: history.slice(0, -1) }),
      });

      const data = await response.json().catch(() => null);
      pendingRow.remove();

      if (!response.ok || !data) {
        appendMessage('assistant', 'Something went wrong talking to the agent. Please try again.');
        return;
      }

      appendMessage('assistant', data.reply || '(no response)', {
        badge: data.usedAtlas ? 'Answered via Atlas' : undefined,
        matches: data.matches,
      });
      history.push({ role: 'assistant', content: data.reply || '' });
    } catch {
      pendingRow.remove();
      appendMessage('assistant', 'Could not reach the server. Is the app running?');
    } finally {
      inputEl.disabled = false;
      sendButton.disabled = false;
      inputEl.focus();
    }
  }

  formEl.addEventListener('submit', (event) => {
    event.preventDefault();
    const message = inputEl.value.trim();
    if (!message) return;
    inputEl.value = '';
    inputEl.style.height = 'auto';
    sendMessage(message);
  });

  inputEl.addEventListener('keydown', (event) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      formEl.requestSubmit();
    }
  });

  inputEl.addEventListener('input', () => {
    inputEl.style.height = 'auto';
    inputEl.style.height = `${inputEl.scrollHeight}px`;
  });

  appendMessage('assistant', "Hi! I'm a general assistant that can also answer Atlas / AI Hub questions. Ask me anything.");
  renderSuggestions();
})();
