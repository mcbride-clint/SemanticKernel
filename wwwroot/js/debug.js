/**
 * BlazorAgentChat — debug.js
 */
window.Debug = {
    async ping() {
        const btn    = document.getElementById('ping-btn');
        const result = document.getElementById('ping-result');
        if (!btn || !result) return;

        btn.disabled    = true;
        result.textContent = 'Pinging…';
        result.className   = 'ping-result';

        try {
            const res  = await fetch('/api/debug/ping', { method: 'POST' });
            const data = await res.json();
            if (data.success) {
                result.textContent = `✓ ${data.elapsedMs} ms — "${data.reply}"`;
                result.classList.add('ping-ok');
            } else {
                result.textContent = `✗ ${data.elapsedMs} ms — ${data.error}`;
                result.classList.add('ping-err');
            }
        } catch (err) {
            result.textContent = `✗ ${err.message}`;
            result.classList.add('ping-err');
        } finally {
            btn.disabled = false;
        }
    }
};
