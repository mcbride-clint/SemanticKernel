/**
 * BlazorAgentChat — chat.js
 * Handles streaming chat, agent selection, file attachments, markdown rendering,
 * conversation history (localStorage), dark mode, and export.
 */
(function () {
    'use strict';

    // ── Constants ────────────────────────────────────────────────────────────
    const HISTORY_KEY    = 'agentchat_history_v2';
    const HISTORY_TTL_MS = 7 * 24 * 60 * 60 * 1000; // 7 days
    const MAX_FILE_BYTES = 10 * 1024 * 1024;          // 10 MB

    // ── State ─────────────────────────────────────────────────────────────────
    let conversationHistory = [];   // ConversationTurn[] kept in sync with server
    let visibleMessages     = [];   // { role, content, agentInfo?, attachmentName? }[]
    let currentAbort        = null; // AbortController for in-flight stream
    let pendingAttachment   = null; // AttachedDocumentDto | null
    let agentPanelOpen      = false;

    // Enabled agent IDs — starts as "all enabled"
    let enabledAgentIds = new Set(
        Array.from(document.querySelectorAll('.agent-checkbox')).map(cb => cb.value)
    );

    // ── Markdown setup ────────────────────────────────────────────────────────
    const mdRenderer = new marked.Renderer();
    marked.setOptions({ renderer: mdRenderer, breaks: true, gfm: true });

    function renderMarkdown(text) {
        try { return marked.parse(text || ''); }
        catch (_) { return escapeHtml(text || ''); }
    }

    function escapeHtml(s) {
        return s.replace(/&/g, '&amp;').replace(/</g, '&lt;')
                .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ── LocalStorage helpers ──────────────────────────────────────────────────
    function saveHistory() {
        try {
            localStorage.setItem(HISTORY_KEY, JSON.stringify({
                ts: Date.now(),
                turns: conversationHistory
            }));
        } catch (_) {}
    }

    function loadHistory() {
        try {
            const raw = localStorage.getItem(HISTORY_KEY);
            if (!raw) return;
            const { ts, turns } = JSON.parse(raw);
            if (Date.now() - ts > HISTORY_TTL_MS) { localStorage.removeItem(HISTORY_KEY); return; }
            conversationHistory = turns || [];
        } catch (_) {}
    }

    // ── DOM helpers ───────────────────────────────────────────────────────────
    const historyEl  = () => document.getElementById('chat-history');
    const inputEl    = () => document.getElementById('chat-input');
    const sendBtn    = () => document.getElementById('send-btn');
    const stopBtn    = () => document.getElementById('stop-btn');

    function scrollToBottom(smooth) {
        const el = historyEl();
        if (!el) return;
        el.scrollTo({ top: el.scrollHeight, behavior: smooth ? 'smooth' : 'instant' });
    }

    function nearBottom() {
        const el = historyEl();
        if (!el) return true;
        return el.scrollHeight - el.scrollTop - el.clientHeight < 120;
    }

    // ── Message rendering ─────────────────────────────────────────────────────
    function appendMessage(msg) {
        visibleMessages.push(msg);
        const container = historyEl();
        if (!container) return;

        const div = document.createElement('div');
        div.className = `chat-entry ${msg.role}`;
        div.dataset.index = visibleMessages.length - 1;

        let html = '';
        if (msg.attachmentName) {
            html += `<div class="entry-attachment">&#128206; ${escapeHtml(msg.attachmentName)}</div>`;
        }

        if (msg.role === 'assistant') {
            html += `<div class="entry-content markdown-content" id="msg-${visibleMessages.length - 1}">${renderMarkdown(msg.content)}</div>`;
            html += `<div class="entry-footer">
                <span class="entry-agent-info" id="agent-info-${visibleMessages.length - 1}"></span>
                <span class="entry-timestamp">${formatTime(new Date())}</span>
                <button class="copy-btn" onclick="Chat.copyMessage(${visibleMessages.length - 1}, this)">&#8853; Copy</button>
            </div>`;
        } else {
            html += `<div class="entry-content">${escapeHtml(msg.content)}</div>`;
            html += `<div class="entry-footer"><span class="entry-timestamp">${formatTime(new Date())}</span></div>`;
        }

        div.innerHTML = html;
        container.appendChild(div);
        scrollToBottom(true);
        return visibleMessages.length - 1;
    }

    function updateStreamingMessage(index, fullText, isThinking) {
        const el = document.getElementById(`msg-${index}`);
        if (!el) return;
        if (isThinking) {
            el.innerHTML = `<span class="spinner"></span><em>${escapeHtml(fullText)}</em>`;
        } else {
            el.innerHTML = renderMarkdown(fullText);
        }
        if (nearBottom()) scrollToBottom(false);
    }

    function setAgentInfo(index, text) {
        const el = document.getElementById(`agent-info-${index}`);
        if (el) el.textContent = text;
    }

    function formatTime(d) {
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    function setLoading(loading) {
        const sb = sendBtn(), st = stopBtn();
        if (sb) sb.style.display = loading ? 'none' : '';
        if (st) st.style.display = loading ? '' : 'none';
        const inp = inputEl();
        if (inp) inp.disabled = loading;
    }

    // ── Agent Panel ───────────────────────────────────────────────────────────
    window.AgentPanel = {
        toggle() {
            agentPanelOpen = !agentPanelOpen;
            const panel = document.getElementById('agent-panel');
            const btn   = document.getElementById('agent-toggle-btn');
            if (panel) panel.classList.toggle('open', agentPanelOpen);
            if (btn)   btn.classList.toggle('open', agentPanelOpen);
            const chevron = btn?.querySelector('.chevron');
            if (chevron) chevron.style.transform = agentPanelOpen ? 'rotate(180deg)' : '';
        },
        enableAll() {
            document.querySelectorAll('.agent-checkbox').forEach(cb => {
                cb.checked = true;
                enabledAgentIds.add(cb.value);
            });
            updateEnabledCount();
        },
        disableAll() {
            document.querySelectorAll('.agent-checkbox').forEach(cb => {
                cb.checked = false;
                enabledAgentIds.delete(cb.value);
            });
            updateEnabledCount();
        },
        toggle(id, checked) {
            if (checked) enabledAgentIds.add(id);
            else         enabledAgentIds.delete(id);
            updateEnabledCount();
        },
        filter(text) {
            const q = text.toLowerCase();
            document.querySelectorAll('.agent-row').forEach(row => {
                const name = row.querySelector('.agent-row-name')?.textContent.toLowerCase() || '';
                const desc = row.querySelector('.agent-row-desc')?.textContent.toLowerCase() || '';
                row.style.display = (name.includes(q) || desc.includes(q)) ? '' : 'none';
            });
            document.querySelectorAll('.agent-group').forEach(group => {
                const anyVisible = Array.from(group.querySelectorAll('.agent-row'))
                    .some(r => r.style.display !== 'none');
                group.style.display = anyVisible ? '' : 'none';
            });
        }
    };

    function updateEnabledCount() {
        const el = document.getElementById('enabled-count');
        if (el) el.textContent = enabledAgentIds.size;
    }

    // ── Attachment handling ───────────────────────────────────────────────────
    window.Attachment = {
        async upload(input) {
            const file = input.files[0];
            if (!file) return;
            if (file.size > MAX_FILE_BYTES) {
                alert(`File too large. Maximum size is ${MAX_FILE_BYTES / 1024 / 1024} MB.`);
                input.value = '';
                return;
            }
            try {
                showAttachmentChip(file.name, null, true);
                const form = new FormData();
                form.append('file', file);
                const res = await fetch('/api/attachment', { method: 'POST', body: form });
                if (!res.ok) throw new Error(`Upload failed: ${res.statusText}`);
                pendingAttachment = await res.json();
                showAttachmentChip(pendingAttachment.fileName, pendingAttachment, false);
            } catch (err) {
                console.error('Attachment upload failed:', err);
                alert('Failed to process attachment: ' + err.message);
                Attachment.clear();
            }
            input.value = '';
        },
        clear() {
            pendingAttachment = null;
            const preview = document.getElementById('attachment-preview');
            if (preview) preview.style.display = 'none';
        }
    };

    function showAttachmentChip(fileName, dto, loading) {
        const preview  = document.getElementById('attachment-preview');
        const thumb    = document.getElementById('attachment-thumbnail');
        const icon     = document.getElementById('attachment-icon');
        const nameEl   = document.getElementById('attachment-filename');

        if (!preview) return;
        preview.style.display = 'flex';
        if (nameEl) nameEl.textContent = loading ? `Processing ${fileName}…` : fileName;

        const isImage = dto?.contentType?.startsWith('image/');
        if (thumb && icon) {
            if (isImage && dto?.base64Bytes) {
                thumb.src = `data:${dto.contentType};base64,${dto.base64Bytes}`;
                thumb.style.display = '';
                icon.style.display  = 'none';
            } else {
                thumb.style.display = 'none';
                icon.style.display  = '';
            }
        }
    }

    // ── Streaming chat ────────────────────────────────────────────────────────
    window.Chat = {
        handleKey(e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                Chat.send();
            }
        },
        autoResize(el) {
            el.style.height = 'auto';
            el.style.height = Math.min(el.scrollHeight, 180) + 'px';
        },
        async send() {
            const inp = inputEl();
            const question = inp?.value.trim();
            if (!question || currentAbort) return;

            inp.value = '';
            inp.style.height = 'auto';

            const attachmentName = pendingAttachment?.fileName || null;
            const attachment     = pendingAttachment ? { ...pendingAttachment } : null;
            Attachment.clear();

            // Render user message
            appendMessage({ role: 'user', content: question, attachmentName });

            // Placeholder assistant message
            const assistantIndex = appendMessage({ role: 'assistant', content: '' });
            let   fullText = '';
            updateStreamingMessage(assistantIndex, 'Thinking…', true);
            setLoading(true);

            currentAbort = new AbortController();

            try {
                const body = {
                    question,
                    enabledAgentIds: [...enabledAgentIds],
                    history: conversationHistory,
                    attachment
                };

                const res = await fetch('/api/chat/stream', {
                    method:  'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body:    JSON.stringify(body),
                    signal:  currentAbort.signal
                });

                if (!res.ok) throw new Error(`Server error: ${res.status} ${res.statusText}`);

                const reader  = res.body.getReader();
                const decoder = new TextDecoder();
                let   buffer  = '';

                while (true) {
                    const { done, value } = await reader.read();
                    if (done) break;

                    buffer += decoder.decode(value, { stream: true });
                    const lines = buffer.split('\n');
                    buffer = lines.pop(); // keep incomplete last line

                    for (const line of lines) {
                        if (!line.startsWith('data: ')) continue;
                        const payload = line.slice(6).trim();

                        if (payload === '[DONE]') { break; }

                        if (payload.startsWith('[HISTORY]')) {
                            try {
                                conversationHistory = JSON.parse(payload.slice(9));
                                saveHistory();
                            } catch (_) {}
                            continue;
                        }

                        if (payload.startsWith('[METADATA]')) {
                            try {
                                const meta = JSON.parse(payload.slice(10));
                                const disclosure = buildDisclosure(meta);
                                if (disclosure) setAgentInfo(assistantIndex, disclosure);
                                visibleMessages[assistantIndex].agentInfo = disclosure;
                            } catch (_) {}
                            continue;
                        }

                        // Regular token
                        try {
                            const token = JSON.parse(payload);
                            fullText += token;
                            updateStreamingMessage(assistantIndex, fullText, false);
                        } catch (_) {}
                    }
                }

                visibleMessages[assistantIndex].content = fullText;

            } catch (err) {
                if (err.name === 'AbortError') {
                    fullText += ' [stopped]';
                } else {
                    console.error('Stream error:', err);
                    fullText = `Error: ${err.message}`;
                }
                updateStreamingMessage(assistantIndex, fullText, false);
                visibleMessages[assistantIndex].content = fullText;
            } finally {
                currentAbort = null;
                setLoading(false);
            }
        },

        stop() {
            currentAbort?.abort();
        },

        async copyMessage(index, btn) {
            const msg = visibleMessages[index];
            if (!msg) return;
            try {
                await navigator.clipboard.writeText(msg.content);
                const orig = btn.innerHTML;
                btn.innerHTML = '&#10003; Copied';
                btn.classList.add('copied');
                setTimeout(() => { btn.innerHTML = orig; btn.classList.remove('copied'); }, 2000);
            } catch (_) {}
        },

        exportHistory() {
            const lines = visibleMessages.map(m => {
                const prefix = m.role === 'user' ? 'You' : 'Assistant';
                return `${prefix}:\n${m.content}${m.agentInfo ? '\n' + m.agentInfo : ''}`;
            });
            const text = lines.join('\n\n---\n\n');
            const blob = new Blob([text], { type: 'text/plain' });
            const url  = URL.createObjectURL(blob);
            const a    = document.createElement('a');
            a.href     = url;
            a.download = `chat-export-${new Date().toISOString().slice(0, 10)}.txt`;
            a.click();
            URL.revokeObjectURL(url);
        },

        clearHistory() {
            if (!confirm('Clear conversation history?')) return;
            conversationHistory = [];
            visibleMessages     = [];
            localStorage.removeItem(HISTORY_KEY);
            const h = historyEl();
            if (h) h.innerHTML = '';
        }
    };

    // ── Metadata → disclosure string ──────────────────────────────────────────
    function buildDisclosure(meta) {
        if (!meta?.selectedAgents?.length) return '';
        const parts = meta.selectedAgents
            .filter(s => s.confidence >= 0.1)
            .sort((a, b) => b.confidence - a.confidence)
            .map(s => {
                const pct = Math.round(s.confidence * 100);
                return s.reason ? `${s.agentId} (${pct}% — ${s.reason})` : `${s.agentId} (${pct}%)`;
            });
        return parts.length ? 'Consulted: ' + parts.join(', ') : '';
    }

    // ── Init ──────────────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {
        loadHistory();
        const inp = inputEl();
        if (inp) inp.focus();
    });

})();
