(function () {
  'use strict';

  const POLL_INTERVAL = 10000;
  let lastRefresh = '';
  let selectedAccountId = '';

  // Tab switching
  document.querySelectorAll('.tab').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-content').forEach(s => s.classList.remove('active'));
      btn.classList.add('active');
      document.getElementById(btn.dataset.tab).classList.add('active');
    });
  });

  // Account selector
  document.getElementById('accountSelector').addEventListener('change', (e) => {
    selectedAccountId = e.target.value;
    lastRefresh = ''; // force full refresh
    refreshStatus();
  });

  function fmt(dateStr) {
    if (!dateStr) return '—';
    const d = new Date(dateStr);
    return d.toLocaleDateString('sv') + ' ' + d.toLocaleTimeString('sv', { hour: '2-digit', minute: '2-digit' });
  }

  function escHtml(str) {
    const el = document.createElement('span');
    el.textContent = str;
    return el.innerHTML;
  }

  function linkifyUrls(text) {
    const escaped = escHtml(text);
    return escaped.replace(/(https?:\/\/[^\s<>"]+)/g, '<a href="$1" target="_blank" rel="noopener">$1</a>');
  }

  async function fetchJson(url) {
    const res = await fetch(url);
    if (!res.ok) return null;
    return res.json();
  }

  function filterByAccount(data) {
    if (!selectedAccountId || !data) return data;
    return data.filter(item => item.accountId === selectedAccountId);
  }

  async function loadAccounts() {
    const data = await fetchJson('/api/accounts');
    if (!data) return;
    const select = document.getElementById('accountSelector');
    const current = select.value;
    // Keep the "All" option, rebuild the rest
    select.innerHTML = '<option value="">All Accounts</option>';
    data.forEach(a => {
      const opt = document.createElement('option');
      opt.value = a.accountId;
      opt.textContent = `${a.displayName} (${a.providerType})`;
      select.appendChild(opt);
    });
    select.value = current;
  }

  async function refreshStatus() {
    const s = await fetchJson('/api/status');
    if (!s) return;

    document.getElementById('username').textContent = s.username;
    document.getElementById('organization').textContent = s.organization;
    document.getElementById('lastRefresh').textContent = s.lastRefresh;
    document.getElementById('rateLimitRemaining').textContent = s.rateLimitRemaining;
    document.getElementById('rateLimitTotal').textContent = s.rateLimitTotal;
    document.getElementById('rateLimitReset').textContent = s.rateLimitResetText;

    const dot = document.getElementById('rateDot');
    const colorMap = { Green: '#22863a', Orange: '#e36209', Red: '#cb2431' };
    dot.style.background = colorMap[s.rateLimitColor] || '#22863a';

    if (s.lastRefresh !== lastRefresh) {
      lastRefresh = s.lastRefresh;
      await Promise.all([
        refreshReviews(),
        refreshPrs(),
        refreshActions(),
        refreshNotifications(),
        refreshIssues(),
        loadAccounts()
      ]);
    }

    // Update badges after filtering
    document.getElementById('reviewBadge').textContent = document.getElementById('reviewsBody').children.length;
    document.getElementById('prBadge').textContent = document.getElementById('prsBody').children.length;
    document.getElementById('actionBadge').textContent = document.getElementById('actionsBody').children.length;
    document.getElementById('notifBadge').textContent = document.getElementById('notificationsBody').children.length;
    document.getElementById('issueBadge').textContent = document.getElementById('issuesBody').children.length;
    document.title = `ScmMoM — ${s.reviewCount} reviews, ${s.prCount} PRs, ${s.actionCount} actions`;
  }

  async function refreshReviews() {
    const raw = await fetchJson('/api/reviews');
    const data = filterByAccount(raw);
    if (!data) return;
    const body = document.getElementById('reviewsBody');
    body.innerHTML = data.map(r => `<tr>
      <td>${escHtml(r.repoName)}</td>
      <td>${r.pullRequestNumber}</td>
      <td title="${escHtml(r.title)}">${escHtml(r.title)}</td>
      <td>${escHtml(r.author)}</td>
      <td>${fmt(r.createdAt)}</td>
      <td><a href="${escHtml(r.url)}" target="_blank" rel="noopener">Open ↗</a></td>
    </tr>`).join('');
  }

  async function refreshPrs() {
    const raw = await fetchJson('/api/pull-requests');
    const data = filterByAccount(raw);
    if (!data) return;
    const body = document.getElementById('prsBody');
    body.innerHTML = data.map(pr => `<tr data-repo="${escHtml(pr.repoName)}" data-number="${pr.number}">
      <td>${escHtml(pr.repoName)}</td>
      <td>${pr.number}</td>
      <td title="${escHtml(pr.title)}">${escHtml(pr.title)}</td>
      <td>${escHtml(pr.author)}</td>
      <td>${escHtml(pr.state)}</td>
      <td>${fmt(pr.updatedAt)}</td>
      <td><a href="${escHtml(pr.url)}" target="_blank" rel="noopener">Open ↗</a></td>
    </tr>`).join('');

    body.querySelectorAll('tr').forEach(row => {
      row.addEventListener('click', (e) => {
        if (e.target.tagName === 'A') return;
        body.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
        row.classList.add('selected');
        loadPrComments(row.dataset.repo, row.dataset.number, row.children[2].textContent);
      });
    });
  }

  async function loadPrComments(repo, number, title) {
    const panel = document.getElementById('prDetail');
    const header = document.getElementById('prDetailHeader');
    const container = document.getElementById('prComments');

    panel.classList.remove('hidden');
    header.textContent = `Comments — ${repo} #${number}: ${title}`;
    container.innerHTML = '<em>Loading...</em>';

    const data = await fetchJson(`/api/pull-requests/${encodeURIComponent(repo)}/${number}/comments`);
    if (!data || data.length === 0) {
      container.innerHTML = '<em style="color:var(--text-secondary)">No comments</em>';
      return;
    }
    container.innerHTML = data.map(c => `<div class="comment">
      <div class="comment-header"><strong>${escHtml(c.author)}</strong> · ${fmt(c.createdAt)}
        ${c.htmlUrl ? ` · <a href="${escHtml(c.htmlUrl)}" target="_blank" rel="noopener">🔗</a>` : ''}
      </div>
      <div class="comment-body">${escHtml(c.body)}</div>
    </div>`).join('');
  }

  async function refreshActions() {
    const raw = await fetchJson('/api/actions');
    const data = filterByAccount(raw);
    if (!data) return;
    const body = document.getElementById('actionsBody');
    body.innerHTML = data.map(a => {
      const isRunning = ['in_progress', 'queued', 'waiting', 'pending'].includes(a.status);
      let conclusionClass = '';
      if (isRunning) conclusionClass = 'conclusion-running';
      else if (a.conclusion === 'success') conclusionClass = 'conclusion-success';
      else if (a.conclusion === 'failure') conclusionClass = 'conclusion-failure';
      else conclusionClass = 'conclusion-cancelled';

      return `<tr data-repo="${escHtml(a.repoName)}" data-checksuite="${a.checkSuiteId}" data-workflow="${escHtml(a.workflowName)}" data-run="${a.runNumber}">
        <td>${escHtml(a.repoName)}</td>
        <td title="${escHtml(a.workflowName)}">${escHtml(a.workflowName)}</td>
        <td>${a.runNumber}</td>
        <td>${escHtml(a.actor)}</td>
        <td>${escHtml(a.status)}</td>
        <td><span class="${conclusionClass}">${escHtml(a.conclusion || a.status)}</span></td>
        <td>${escHtml(a.branch)}</td>
        <td>${fmt(a.createdAt)}</td>
        <td><a href="${escHtml(a.url)}" target="_blank" rel="noopener">Open ↗</a></td>
      </tr>`;
    }).join('');

    body.querySelectorAll('tr').forEach(row => {
      row.addEventListener('click', (e) => {
        if (e.target.tagName === 'A') return;
        body.querySelectorAll('tr').forEach(r => r.classList.remove('selected'));
        row.classList.add('selected');
        loadAnnotations(row.dataset.repo, row.dataset.checksuite, row.dataset.workflow, row.dataset.run);
      });
    });
  }

  async function loadAnnotations(repo, checkSuiteId, workflow, runNumber) {
    const panel = document.getElementById('actionDetail');
    const header = document.getElementById('actionDetailHeader');
    const container = document.getElementById('actionAnnotations');

    panel.classList.remove('hidden');
    header.textContent = `Annotations — ${workflow} #${runNumber} (${repo})`;
    container.innerHTML = '<em>Loading...</em>';

    const data = await fetchJson(`/api/actions/${encodeURIComponent(repo)}/${checkSuiteId}/annotations`);
    if (!data || data.length === 0) {
      container.innerHTML = '<em style="color:var(--text-secondary)">No annotations found.</em>';
      return;
    }
    container.innerHTML = data.map(a => `<div class="annotation">
      <div class="annotation-header">
        <span class="annotation-level ${a.level}">${escHtml(a.level)}</span>
        <strong>${escHtml(a.checkRunName)}</strong>
        <span style="color:var(--text-secondary);margin-left:8px">${escHtml(a.path)}:${a.startLine}</span>
      </div>
      ${a.title ? `<div style="font-weight:600;margin:2px 0">${escHtml(a.title)}</div>` : ''}
      <div class="annotation-message">${linkifyUrls(a.message)}</div>
    </div>`).join('');
  }

  async function refreshNotifications() {
    const raw = await fetchJson('/api/notifications');
    const data = filterByAccount(raw);
    if (!data) return;
    const body = document.getElementById('notificationsBody');
    body.innerHTML = data.map(n => `<tr>
      <td>${escHtml(n.repoName || '')}</td>
      <td>${escHtml(n.type || '')}</td>
      <td title="${escHtml(n.title)}">${escHtml(n.title)}</td>
      <td>${escHtml(n.reason || '')}</td>
      <td>${fmt(n.updatedAt)}</td>
      <td>${n.url ? `<a href="${escHtml(n.url)}" target="_blank" rel="noopener">Open ↗</a>` : '—'}</td>
    </tr>`).join('');
  }

  async function refreshIssues() {
    const raw = await fetchJson('/api/issues');
    const data = filterByAccount(raw);
    if (!data) return;
    const body = document.getElementById('issuesBody');
    body.innerHTML = data.map(i => `<tr>
      <td>${escHtml(i.repoName || '')}</td>
      <td>${i.number || ''}</td>
      <td title="${escHtml(i.title)}">${escHtml(i.title)}</td>
      <td>${escHtml(i.author || '')}</td>
      <td>${escHtml(i.state || '')}</td>
      <td>${escHtml((i.labels || []).join(', '))}</td>
      <td>${fmt(i.updatedAt)}</td>
      <td>${i.url ? `<a href="${escHtml(i.url)}" target="_blank" rel="noopener">Open ↗</a>` : '—'}</td>
    </tr>`).join('');
  }

  // Initial load + polling
  refreshStatus();
  setInterval(refreshStatus, POLL_INTERVAL);
})();
